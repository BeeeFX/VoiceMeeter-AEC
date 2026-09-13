// GPL-3.0-only; ASIO SDK used under its GPLv3 option.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <objbase.h>
#include "iasiodrv.h"
#include <atomic>
#include <vector>
#include <cstdio>
#include <cstring>
#include <cmath>
#include <algorithm>
#include <conio.h>
#include <chrono>
#include <cstdint>
extern "C" void aec_block(void*,const float*,const float*,const float*,float*,size_t,int,float*);
namespace {
bool route_active(float route,float sm,float bm,float sg,float bg,float layer,bool anysolo,float solo){return route>0.5f&&sm<0.5f&&bm<0.5f&&sg>-60.f&&bg>-60.f&&layer>-60.f&&(!anysolo||solo>0.5f);}
int combine_route_states(int a,int b){return a==1||b==1?1:a<0||b<0?-1:0;}
IASIO* driver=nullptr; void* engine=nullptr;
std::vector<ASIOBufferInfo> buffers;
std::vector<ASIOSampleType> formats;
std::vector<float> micdata,leftdata,rightdata,clean;
long channels=0,frames=0; int mic=0; uint64_t reference_left=0,reference_right=0,returns=0;
bool ready=false;
std::atomic<int> mode{0},fault{0};
std::atomic<uint64_t> callbacks{0},overruns{0},max_us{0};
std::atomic<float> micpeak{0},refpeak{0},missing{0},errors{0};
std::atomic<bool> quitting{false}; std::atomic_flag busy=ATOMIC_FLAG_INIT;
std::atomic<bool> latency_dirty{false};
void reset_session_state(int initial){
 fault=0;quitting=false;callbacks=0;overruns=0;max_us=0;micpeak=0;refpeak=0;missing=0;errors=0;
 busy.clear();ready=false;latency_dirty=false;mode=initial==3?0:initial;
}
int completion_code(bool cancelled,int driver_fault){return cancelled?0:driver_fault?30+driver_fault:0;}
// The launcher owns an anonymous stdin pipe. Never block the ASIO control loop.
int read_control(){
 HANDLE input=GetStdHandle(STD_INPUT_HANDLE);
 if(GetFileType(input)==FILE_TYPE_PIPE){
  DWORD available=0,read=0;char c=0;
  if(PeekNamedPipe(input,nullptr,0,nullptr,&available,nullptr)&&available&&ReadFile(input,&c,1,&read,nullptr)&&read==1)return c;
  return -1;
 }
 return GetFileType(input)==FILE_TYPE_CHAR&&_kbhit()?_getch():-1;
}
void apply_control(int c,bool& automatic,int auto_mask,int& laststate){
 if(c=='q'||c=='Q'){quitting=true;return;}
 if(c=='a'||c=='A'){automatic=false;mode=0;}
 if(c=='b'||c=='B'){automatic=false;mode=1;}
 if(c=='m'||c=='M'){automatic=false;mode=2;}
 if((c=='t'||c=='T')&&auto_mask>0){automatic=true;laststate=-2;}
 if(c=='a'||c=='A'||c=='b'||c=='B'||c=='m'||c=='M'||c=='t'||c=='T')std::printf("Control: mode=%d auto=%d\n",mode.load(),int(automatic));
}
int bytes(ASIOSampleType t) {return t==ASIOSTFloat32LSB||t==ASIOSTInt32LSB?4:t==ASIOSTInt24LSB?3:t==ASIOSTInt16LSB?2:0;}
float read_sample(const void* base,int i,ASIOSampleType t) {
 auto p=static_cast<const unsigned char*>(base)+size_t(i)*bytes(t);
 if(t==ASIOSTFloat32LSB){float v;memcpy(&v,p,4);return v;}
 if(t==ASIOSTInt32LSB){int32_t v;memcpy(&v,p,4);return float(double(v)/2147483648.);}
 if(t==ASIOSTInt16LSB){int16_t v;memcpy(&v,p,2);return float(v)/32768.f;}
 int32_t v=int32_t(p[0])|(int32_t(p[1])<<8)|(int32_t(p[2])<<16);
 if(v&0x800000)v|=int32_t(0xff000000);return float(v)/8388608.f;
}
void write_sample(void* base,int i,ASIOSampleType t,float v) {
 auto p=static_cast<unsigned char*>(base)+size_t(i)*bytes(t);
 v=std::isfinite(v)?std::clamp(v,-1.f,1.f):0.f;
 if(t==ASIOSTFloat32LSB){memcpy(p,&v,4);return;}
 if(t==ASIOSTInt32LSB){int32_t x=int32_t(std::clamp(double(v)*2147483648.,-2147483648.,2147483647.));memcpy(p,&x,4);return;}
 if(t==ASIOSTInt16LSB){int16_t x=int16_t(std::clamp(double(v)*32768.,-32768.,32767.));memcpy(p,&x,2);return;}
 int32_t x=int32_t(std::clamp(double(v)*8388608.,-8388608.,8388607.));p[0]=x&255;p[1]=(x>>8)&255;p[2]=(x>>16)&255;
}
void transfer(long index) {
 for(long c=0;c<channels;c++) {
  if(!(returns&(uint64_t(1)<<c)))memcpy(buffers[channels+c].buffers[index],buffers[c].buffers[index],size_t(frames)*bytes(formats[c]));
  else for(int i=0;i<frames;i++)write_sample(buffers[channels+c].buffers[index],i,formats[c],clean[i]);
 }
}
void process(long index,ASIOBool) {
 if(index<0||index>1){fault=1;return;}
 if(busy.test_and_set(std::memory_order_acquire)){fault=2;return;}
 auto begin=std::chrono::steady_clock::now();
 for(int i=0;i<frames;i++){
  micdata[i]=read_sample(buffers[mic].buffers[index],i,formats[mic]);
  float mixed_left=0.f,mixed_right=0.f;
  for(int c=0;c<channels;c++){
   if(reference_left&(uint64_t(1)<<c))mixed_left+=read_sample(buffers[c].buffers[index],i,formats[c]);
   if(reference_right&(uint64_t(1)<<c))mixed_right+=read_sample(buffers[c].buffers[index],i,formats[c]);
  }
  leftdata[i]=std::clamp(mixed_left,-1.f,1.f);
  rightdata[i]=std::clamp(mixed_right,-1.f,1.f);
 }
 float stats[4]{};
 aec_block(engine,micdata.data(),leftdata.data(),rightdata.data(),clean.data(),size_t(frames),mode.load(),stats);
 transfer(index);if(ready && driver->outputReady()!=ASE_OK)fault=6;
 micpeak=stats[0];refpeak=stats[1];missing=stats[2];errors=stats[3];
 auto us=uint64_t(std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-begin).count());
 if(us>max_us.load())max_us=us;if(us*48000>uint64_t(frames)*1000000)overruns++;callbacks++;
 busy.clear(std::memory_order_release);
}
ASIOTime* process_time(ASIOTime* t,long i,ASIOBool direct){process(i,direct);return t;}
void rate_changed(ASIOSampleRate rate){if(rate!=48000.)fault=3;}
long message(long selector,long value,void*,double*) {
 if(selector==kAsioSelectorSupported)return value==kAsioEngineVersion||value==kAsioResetRequest||value==kAsioResyncRequest||value==kAsioLatenciesChanged||value==kAsioSupportsTimeInfo;
 if(selector==kAsioEngineVersion)return 2;if(selector==kAsioSupportsTimeInfo)return 1;
 if(selector==kAsioLatenciesChanged){latency_dirty=true;return 1;}
 if(selector==kAsioResetRequest||selector==kAsioResyncRequest){fault=4;return 1;}return 0;
}
BOOL WINAPI ctrl(DWORD){quitting=true;return TRUE;}
bool ok(ASIOError e,const char* stage){if(e==ASE_OK)return true;std::fprintf(stderr,"ASIO %s: error %ld\n",stage,long(e));return false;}
// Read-only Remote API. Never resolve or call any SetParameter function.
struct Remote {
 HMODULE dll=nullptr;using Simple=long(__stdcall*)();using Get=long(__stdcall*)(char*,float*);
 Simple logout=nullptr,dirty=nullptr;Get get=nullptr;bool logged=false;
 bool open(){
  dll=LoadLibraryExW(L"C:\\Program Files (x86)\\VB\\Voicemeeter\\VoicemeeterRemote64.dll",nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
  if(!dll)return false;
  auto login=reinterpret_cast<Simple>(GetProcAddress(dll,"VBVMR_Login"));
  logout=reinterpret_cast<Simple>(GetProcAddress(dll,"VBVMR_Logout"));
  dirty=reinterpret_cast<Simple>(GetProcAddress(dll,"VBVMR_IsParametersDirty"));
  get=reinterpret_cast<Get>(GetProcAddress(dll,"VBVMR_GetParameterFloat"));
  if(!login||!logout||!dirty||!get)return false;
  long result=login();logged=result>=0;return result==0;
 }
 int state(int strip,int bus){
  if(!logged||!dirty||dirty()<0)return -1;
  char key[80];float route=0,sm=0,bm=0,sg=0,bg=0,solo=0,layer=0;bool anysolo=false;
  auto read=[&](const char* kind,int index,const char* field,float& out){std::snprintf(key,sizeof(key),"%s[%d].%s",kind,index,field);return get(key,&out)==0&&std::isfinite(out);};
  char routekey[8];std::snprintf(routekey,sizeof(routekey),"A%d",bus+1);
  if(!read("Strip",strip,routekey,route)||!read("Strip",strip,"Mute",sm)||!read("Bus",bus,"Mute",bm)||!read("Strip",strip,"Gain",sg)||!read("Bus",bus,"Gain",bg))return -1;
  for(int i=0;i<8;i++){float s=0;if(!read("Strip",i,"Solo",s))return -1;if(s>0.5f)anysolo=true;if(i==strip)solo=s;}
  char layerkey[32];std::snprintf(layerkey,sizeof(layerkey),"GainLayer[%d]",bus);if(!read("Strip",strip,layerkey,layer))return -1; return route_active(route,sm,bm,sg,bg,layer,anysolo,solo)?1:0;
 }
 int state_mask(int mask,int bus){
  int result=0;
  for(int i=0;i<8;i++)if(mask&(1<<i)){result=combine_route_states(result,state(i,bus));if(result==1)return 1;}
  return result;
 }
 ~Remote(){if(logged&&logout)logout();if(dll)FreeLibrary(dll);}
};
}
extern "C" int asio_run(void* ctx,int m,uint64_t ref_left,uint64_t ref_right,uint64_t ret,int initial,int seconds,int probe,int auto_mask,int auto_bus,int* final_mode){
 setvbuf(stdout,nullptr,_IONBF,0);
 struct HostWindow { HWND h; ~HostWindow(){if(h)DestroyWindow(h);} } hostWindow{
  CreateWindowExW(0,L"STATIC",L"VoiceMeeter AEC",0,0,0,0,0,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr)
 };
 struct InstanceGuard {HANDLE h;~InstanceGuard(){if(h)CloseHandle(h);}} instance{CreateMutexW(nullptr,FALSE,L"Local\\PotatoAECInsertPrototype")}; if(!instance.h||GetLastError()==ERROR_ALREADY_EXISTS){std::fprintf(stderr,"VoiceMeeter AEC is already running.\n");return 22;}
 reset_session_state(initial);
 HKEY key=nullptr;
 if(RegOpenKeyExW(HKEY_LOCAL_MACHINE,L"SOFTWARE\\ASIO\\Voicemeeter Potato Insert Virtual ASIO",0,KEY_READ|KEY_WOW64_64KEY,&key)!=ERROR_SUCCESS){std::fprintf(stderr,"Potato Insert x64 driver is missing.\n");return 10;}
 wchar_t text[128]{};DWORD size=sizeof(text),type=0;
 auto rr=RegQueryValueExW(key,L"CLSID",nullptr,&type,reinterpret_cast<BYTE*>(text),&size);RegCloseKey(key);CLSID id{};
 if(rr!=ERROR_SUCCESS||type!=REG_SZ||FAILED(CLSIDFromString(text,&id)))return 11;
 if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 12;
 int result=0;bool created=false,started=false;Remote remote;bool automatic=initial==3;
 do {
  if(FAILED(CoCreateInstance(id,nullptr,CLSCTX_INPROC_SERVER,id,reinterpret_cast<void**>(&driver)))){result=13;break;}
  if(!hostWindow.h||!driver->init(hostWindow.h)){result=14;break;}
  long ins=0,outs=0,min=0,max=0,preferred=0,gran=0,inlat=0,outlat=0;double rate=0;
  if(!ok(driver->getChannels(&ins,&outs),"channels")||!ok(driver->getSampleRate(&rate),"sample rate")||!ok(driver->getBufferSize(&min,&max,&preferred,&gran),"buffer")){result=15;break;}
  driver->getLatencies(&inlat,&outlat);
  std::printf("Insert Potato: %ld inputs / %ld outputs; %.0f Hz; buffer %ld (min %ld max %ld).\nDriver-reported latency: input %ld, output %ld samples.\n",ins,outs,rate,preferred,min,max,inlat,outlat);
  if(ins!=outs||ins<1||ins>34||preferred<1||preferred>8192||rate!=48000.){result=16;break;}
  channels=ins;frames=preferred;formats.resize(channels);bool valid=true;
  for(long c=0;c<channels;c++){
   ASIOChannelInfo a{},b{};a.channel=b.channel=c;a.isInput=ASIOTrue;b.isInput=ASIOFalse;
   if(!ok(driver->getChannelInfo(&a),"input format")||!ok(driver->getChannelInfo(&b),"output format")){valid=false;break;}
   std::printf("%2ld : %-32.32s type %ld\n",c+1,a.name,long(a.type));
   if(bytes(a.type)==0||a.type!=b.type)valid=false;formats[c]=a.type;
  }
  if(!valid){std::fprintf(stderr,"Unsupported format; no stream opened.\n");result=17;break;}
  if(probe)break;
  if(!ctx||m<0||m>=channels||ref_left==0||ref_right==0||((ref_left|ref_right|ret)>>channels)!=0){result=18;break;}
  engine=ctx;mic=m;reference_left=ref_left;reference_right=ref_right;returns=ret;mode=initial==3?0:initial;
  if(auto_mask>0&&!remote.open()){std::fprintf(stderr,"Remote API unavailable: Auto cannot read routing; Auto mode keeps AEC on.\n");if(automatic)mode=0;}
  micdata.resize(frames);leftdata.resize(frames);rightdata.resize(frames);clean.resize(frames);buffers.resize(channels*2);
  for(long c=0;c<channels*2;c++){buffers[c]={};buffers[c].isInput=c<channels?ASIOTrue:ASIOFalse;buffers[c].channelNum=c%channels;}
  ASIOCallbacks cb{process,rate_changed,message,process_time};
  if(!ok(driver->createBuffers(buffers.data(),channels*2,frames,&cb),"buffer creation")){result=19;break;}created=true;
  for(const auto& buffer:buffers)if(!buffer.buffers[0]||!buffer.buffers[1])valid=false;
  if(!valid){std::fprintf(stderr,"Driver returned a null audio buffer; refusing to start.\n");result=19;break;}
  for(long c=channels;c<channels*2;c++)for(int b=0;b<2;b++)memset(buffers[c].buffers[b],0,size_t(frames)*bytes(formats[c-channels]));
  ready=driver->outputReady()==ASE_OK;SetConsoleCtrlHandler(ctrl,TRUE);
  if(!ok(driver->start(),"start")){result=20;break;}started=true;
  std::puts("Running. A=AEC B=bypass M=mute T=auto Q=quit. Disable PATCH INSERT before Q.");
  auto begin=std::chrono::steady_clock::now(),last=begin,lastauto=begin;uint64_t previous=0;int laststate=-2;
  while(!quitting&&!fault){
   if(latency_dirty.exchange(false)){
    long newin=0,newout=0;
    if(ok(driver->getLatencies(&newin,&newout),"latency refresh"))std::printf("Updated driver latency: input %ld, output %ld samples.\n",newin,newout);
   }
   MSG msg;while(PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)){TranslateMessage(&msg);DispatchMessageW(&msg);}
   apply_control(read_control(),automatic,auto_mask,laststate);
   if(quitting)break;
   auto now=std::chrono::steady_clock::now();
   if(automatic&&now-lastauto>=std::chrono::milliseconds(200)){
    int state=remote.state_mask(auto_mask,auto_bus);mode=state==0?1:0;
    if(state!=laststate)std::printf("Auto strips mask 0x%02x -> A%d : %s\n",auto_mask,auto_bus+1,state<0?"read unavailable, AEC on":state?"AEC on":"bypass");
    laststate=state;lastauto=now;
   }
   if(seconds>0&&now-begin>=std::chrono::seconds(seconds))break;
   if(now-last>=std::chrono::seconds(2)){
    auto count=callbacks.load();
    std::printf("mode=%d auto=%d blocks=%llu max=%llu us overruns=%llu mic=%.4f ref=%.4f ref_missing=%d errors=%.0f\n",mode.load(),int(automatic),count,max_us.load(),overruns.load(),micpeak.load(),refpeak.load(),int(missing.load()),errors.load());
    if(count==previous){std::fprintf(stderr,"No callback for 2 seconds.\n");fault=5;}previous=count;last=now;
   }Sleep(10);
  }
  result=completion_code(quitting.load(),fault.load());
  if(result){std::fprintf(stderr,"Driver requested stop / interruption: %d\n",fault.load());}
 }while(false);
 if(final_mode)*final_mode=automatic?3:mode.load();
 if(started)driver->stop();if(created)driver->disposeBuffers();if(driver){driver->Release();driver=nullptr;}
 SetConsoleCtrlHandler(ctrl,FALSE);CoUninitialize();std::puts("Closed. If the strip is silent, disable its PATCH INSERT.");return result;
}
extern "C" int asio_transport_test(){
 if(combine_route_states(0,0)!=0||combine_route_states(0,1)!=1||combine_route_states(-1,0)!=-1||combine_route_states(-1,1)!=1||combine_route_states(1,-1)!=1)return 10;
 if(completion_code(true,4)!=0||completion_code(true,5)!=0||completion_code(false,4)!=34)return 8;
 reset_session_state(2);
 if(mode.load()!=2||callbacks.load()!=0||fault.load()!=0)return 5;
 if(message(kAsioLatenciesChanged,0,nullptr,nullptr)!=1||fault.load()!=0||!latency_dirty.load())return 9;
 if(message(kAsioResetRequest,0,nullptr,nullptr)!=1||fault.load()!=4)return 6;
 reset_session_state(1);rate_changed(44100.);if(fault.load()!=3||mode.load()!=1)return 7;
 reset_session_state(0);
 if(!route_active(1,0,0,0,0,0,false,0)||route_active(0,0,0,0,0,0,false,0)||route_active(1,1,0,0,0,0,false,0)||route_active(1,0,1,0,0,0,false,0)||route_active(1,0,0,-60,0,0,false,0)||route_active(1,0,0,0,0,-60,false,0)||route_active(1,0,0,0,0,0,true,0)||!route_active(1,0,0,0,0,0,true,1))return 4;
 for(auto t:{ASIOSTFloat32LSB,ASIOSTInt32LSB,ASIOSTInt24LSB,ASIOSTInt16LSB}){
  unsigned char b[32]{};int i=0;for(float v:{-1.f,-0.5f,0.f,0.123f,0.999f,1.f}){write_sample(b,i,t,v);if(std::abs(read_sample(b,i,t)-v)>0.00004f)return 1;i++;}
  channels=34;frames=192;returns=3;formats.assign(34,t);buffers.assign(68,{});clean.assign(192,0.25f);
  std::vector<std::vector<unsigned char>> data(136,std::vector<unsigned char>(192*bytes(t)));
  for(int c=0;c<68;c++)for(int j=0;j<2;j++){buffers[c].buffers[j]=data[c*2+j].data();for(size_t k=0;k<data[c*2+j].size();k++)data[c*2+j][k]=static_cast<unsigned char>(c*7+k+j);}
  for(int j=0;j<2;j++){transfer(j);for(int c=2;c<34;c++)if(data[c*2+j]!=data[(c+34)*2+j])return 2;
   for(int c=0;c<2;c++)for(int k=0;k<192;k++)if(std::abs(read_sample(buffers[34+c].buffers[j],k,t)-0.25f)>0.00004f)return 3;
  }
 }std::puts("Transport: 34 channels, double buffer, 4 formats, other channels unchanged: OK.");return 0;
}

// Integration test for the launcher's actual anonymous pipe, without a driver.
extern "C" int asio_control_selftest(){
 setvbuf(stdout,nullptr,_IONBF,0);reset_session_state(0);
 bool automatic=false;int laststate=0,count=0;
 const char expected[]="mbatq";
 auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(4);
 std::puts("Running. Control test only; no audio.");
 while(count<5&&std::chrono::steady_clock::now()<deadline){
  int c=read_control();if(c<0||c=='\r'||c=='\n'){Sleep(5);continue;}
  if(c!=expected[count])return 1;
  apply_control(c,automatic,5,laststate);
  if((count==0&&mode!=2)||(count==1&&mode!=1)||(count==2&&mode!=0)||(count==3&&!automatic)||(count==4&&!quitting))return 2;
  count++;
 }
 if(count!=5)return 3;
 std::puts("Control pipe: PASS");return 0;
}

// Exercises the real native callback and Rust FFI without opening any driver.
extern "C" int asio_callback_selftest(void* ctx){
 reset_session_state(1);engine=ctx;channels=34;frames=192;mic=0;reference_left=(uint64_t(1)<<10)|(uint64_t(1)<<18);reference_right=(uint64_t(1)<<11)|(uint64_t(1)<<19);returns=3;
 formats.assign(34,ASIOSTFloat32LSB);buffers.assign(68,{});
 micdata.assign(frames,0);leftdata.assign(frames,0);rightdata.assign(frames,0);clean.assign(frames,0);
 std::vector<std::vector<float>> data(136,std::vector<float>(frames));
 for(int c=0;c<68;c++)for(int j=0;j<2;j++)buffers[c].buffers[j]=data[c*2+j].data();
 for(int block=0;block<300;block++){
  int j=block%2;
  for(int c=0;c<34;c++)for(int i=0;i<frames;i++)data[c*2+j][i]=c==0?0.25f:float(c+1)/100.f;
  if(block==200)mode=2;
  process(j,ASIOTrue);
  for(int c=2;c<34;c++)if(data[c*2+j]!=data[(c+34)*2+j])return 1;
  for(int c=0;c<2;c++)for(int i=0;i<frames;i++){
   float expected=(block*frames+i<480||block>=200)?0.f:0.25f;
   if(data[(c+34)*2+j][i]!=expected)return 2;
  }
 }
 if(callbacks.load()!=300||fault.load()!=0)return 3;
 if(std::abs(refpeak.load()-0.32f)>0.0001f)return 5;
 process(2,ASIOTrue);if(fault.load()!=1)return 4;
 std::puts("Full callback/FFI: framing, 34-channel preservation, immediate mute and invalid-index detection PASS.");
 return 0;
}


extern "C" int remote_check(){
 Remote remote;if(!remote.open()){std::puts("Remote API unavailable");return 1;}
 Sleep(100);int state=remote.state(5,1);
 std::printf("Read-only: Strip[5] (VAIO, strip 6) -> Bus[1] A2 = %s\n",state<0?"unknown":state?"AEC on":"bypass");
 char key[64];for(const char* field:{"A2","Mute","Gain","GainLayer[1]","Solo"}){float v=0;std::snprintf(key,sizeof(key),"Strip[5].%s",field);long r=remote.get(key,&v);std::printf("%s = %.2f (code %ld)\n",key,v,r);}
 float v=0;char busmute[]="Bus[1].Mute";remote.get(busmute,&v);std::printf("Bus[1].Mute = %.2f\n",v);
 return state<0?1:0;
}

