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
#include <array>
extern "C" void aec_block(void*,const float*,const float*,const float*,float*,size_t,int,float*);
extern "C" void* aec_create(int,int,int,int);
extern "C" void aec_destroy(void*);
namespace {
bool route_active(float route,float sm,float bm,float sg,float bg,float layer,bool anysolo,float solo){return route>0.5f&&sm<0.5f&&bm<0.5f&&sg>-60.f&&bg>-60.f&&layer>-60.f&&(!anysolo||solo>0.5f);}
int combine_route_states(int a,int b){return a==1||b==1?1:a<0||b<0?-1:0;}
IASIO* driver=nullptr; void* engine=nullptr;
std::vector<ASIOBufferInfo> buffers;
std::vector<ASIOSampleType> formats;
std::vector<float> micdata,leftdata,rightdata,clean;
long channels=0,frames=0; int mic=0; uint64_t reference_left=0,reference_right=0,returns=0;
long session_rate=48000;
bool ready=false;
std::atomic<int> mode{0},fault{0};
std::atomic<uint64_t> callbacks{0},overruns{0},max_us{0};
std::atomic<float> micpeak{0},refpeak{0},missing{0},errors{0};
std::atomic<float> dsp_failed{0};
// The control thread publishes normalized weights. The callback never calls Remote.
std::array<std::atomic<float>,34> published_weights{};
std::atomic<unsigned> weights_version{0};
std::array<float,34> current_weights{},target_weights{};
int weight_ramp=0;
int channel_strip(int c,int count){return count==22?(c<6?c/2:c<14?3:4):(c<10?c/2:c<18?5:c<26?6:7);}
float finite_sample(float v){return std::isfinite(v)?std::clamp(v,-1.f,1.f):0.f;}
std::array<float,34> normalized_weights(uint64_t left,uint64_t right,const std::array<float,34>& gains){
 float l=0,r=0;std::array<float,34> result{};
 for(int c=0;c<34;c++){
  float gain=std::isfinite(gains[c])?std::max(0.f,gains[c]):0.f;
  if(left&(uint64_t(1)<<c))l+=gain;
  if(right&(uint64_t(1)<<c))r+=gain;
  if((left|right)&(uint64_t(1)<<c))result[c]=gain;
 }
 // A common scale preserves stereo balance and relative strip levels.
 float divisor=std::max(1.f,std::max(l,r));for(auto& gain:result)gain/=divisor;return result;
}
void publish_weights(const std::array<float,34>& weights){
 weights_version.fetch_add(1);
 for(int c=0;c<34;c++)published_weights[c].store(weights[c]);
 weights_version.fetch_add(1);
}
void initialize_weights(){
 std::array<float,34> unity;unity.fill(1.f);
 current_weights=target_weights=normalized_weights(reference_left,reference_right,unity);
 publish_weights(target_weights);weight_ramp=0;
}
std::atomic<bool> quitting{false}; std::atomic_flag busy=ATOMIC_FLAG_INIT;
std::atomic<bool> latency_dirty{false};
void reset_session_state(int initial){
 fault=0;quitting=false;callbacks=0;overruns=0;max_us=0;micpeak=0;refpeak=0;missing=0;errors=0;dsp_failed=0;
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
 // A bounded snapshot: if publication overlaps this callback, retain the last one.
 auto version=weights_version.load();std::array<float,34> snapshot{};
 if(!(version&1)){
  for(int c=0;c<34;c++)snapshot[c]=published_weights[c].load();
  if(version==weights_version.load()&&snapshot!=target_weights){target_weights=snapshot;weight_ramp=480;}
 }
 for(int i=0;i<frames;i++){
  micdata[i]=read_sample(buffers[mic].buffers[index],i,formats[mic]);
  float mixed_left=0.f,mixed_right=0.f;
  for(int c=0;c<channels;c++){
   if(weight_ramp)current_weights[c]+=(target_weights[c]-current_weights[c])/float(weight_ramp);
   if(reference_left&(uint64_t(1)<<c))mixed_left+=finite_sample(read_sample(buffers[c].buffers[index],i,formats[c]))*current_weights[c];
   if(reference_right&(uint64_t(1)<<c))mixed_right+=finite_sample(read_sample(buffers[c].buffers[index],i,formats[c]))*current_weights[c];
  }
  if(weight_ramp)--weight_ramp;
  leftdata[i]=std::clamp(mixed_left,-1.f,1.f);
  rightdata[i]=std::clamp(mixed_right,-1.f,1.f);
 }
 float stats[5]{};
 aec_block(engine,micdata.data(),leftdata.data(),rightdata.data(),clean.data(),size_t(frames),mode.load(),stats);
 transfer(index);if(ready && driver->outputReady()!=ASE_OK)fault=6;
 micpeak=stats[0];refpeak=stats[1];missing=stats[2];errors=stats[3];dsp_failed=stats[4];
 auto us=uint64_t(std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now()-begin).count());
 if(us>max_us.load())max_us=us;if(us*uint64_t(session_rate)>uint64_t(frames)*1000000)overruns++;callbacks++;
 busy.clear(std::memory_order_release);
}
ASIOTime* process_time(ASIOTime* t,long i,ASIOBool direct){process(i,direct);return t;}
void rate_changed(ASIOSampleRate rate){if(std::abs(rate-double(session_rate))>0.5)fault=3;}
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
 Simple logout=nullptr,dirty=nullptr;Get get=nullptr;bool logged=false;int strips=8;
 explicit Remote(int strip_count=8):strips(strip_count){}
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
 int state(int strip,int bus,float* linear_gain=nullptr){
  if(!logged||!dirty||dirty()<0)return -1;
  char key[80];float route=0,sm=0,bm=0,sg=0,bg=0,solo=0,layer=0;bool anysolo=false;
  auto read=[&](const char* kind,int index,const char* field,float& out){std::snprintf(key,sizeof(key),"%s[%d].%s",kind,index,field);return get(key,&out)==0&&std::isfinite(out);};
  char routekey[8];std::snprintf(routekey,sizeof(routekey),"A%d",bus+1);
  if(!read("Strip",strip,routekey,route)||!read("Strip",strip,"Mute",sm)||!read("Bus",bus,"Mute",bm)||!read("Strip",strip,"Gain",sg)||!read("Bus",bus,"Gain",bg))return -1;
  for(int i=0;i<strips;i++){float s=0;if(!read("Strip",i,"Solo",s))return -1;if(s>0.5f)anysolo=true;if(i==strip)solo=s;}
  // GainLayer is the selected bus's strip fader on Potato; Banana has only Gain.
  if(strips==8){char layerkey[32];std::snprintf(layerkey,sizeof(layerkey),"GainLayer[%d]",bus);if(!read("Strip",strip,layerkey,layer))return -1;sg=layer;}
  bool active=route_active(route,sm,bm,sg,bg,0,anysolo,solo);
  if(linear_gain)*linear_gain=active?std::pow(10.f,std::clamp(sg+bg,-120.f,24.f)/20.f):0.f;
  return active?1:0;
 }
 bool reference_weights(int bus){
  std::array<float,34> gains{};std::array<float,8> strip_gains{};std::array<int,8> states{};states.fill(-2);bool known=true;
  for(int c=0;c<channels;c++)if((reference_left|reference_right)&(uint64_t(1)<<c)){
   int strip=channel_strip(c,channels);
   if(states[strip]==-2)states[strip]=state(strip,bus,&strip_gains[strip]);
   if(states[strip]<0)known=false;
   gains[c]=strip_gains[strip];
  }
  // Do not silently discard echo sources when routing is unavailable.
  if(!known)gains.fill(1.f);
  publish_weights(normalized_weights(reference_left,reference_right,gains));return known;
 }
 int state_mask(int mask,int bus){
  int result=0;
  for(int i=0;i<strips;i++)if(mask&(1<<i)){result=combine_route_states(result,state(i,bus));if(result==1)return 1;}
  return result;
 }
 ~Remote(){if(logged&&logout)logout();if(dll)FreeLibrary(dll);}
};
// Deterministic Remote fixture; no DLL is loaded and no mixer settings are changed.
bool test_potato=false,test_mute=false,test_layer_read=false;
long __stdcall test_dirty(){return 0;}
long __stdcall test_get(char* key,float* out){
 *out=0;
 if(std::strstr(key,"GainLayer")){test_layer_read=true;if(!test_potato)return -1;*out=-6.f;}
 else if(std::strstr(key,".Gain"))*out=std::strncmp(key,"Strip",5)==0?-12.f:0.f;
 else if(std::strstr(key,".A2"))*out=1.f;
 else if(std::strstr(key,".Mute"))*out=test_mute?1.f:0.f;
 return 0;
}
bool reference_selftest(){
 for(int strips:{5,8}){
  Remote remote(strips);remote.logged=true;remote.dirty=test_dirty;remote.get=test_get;
  test_potato=strips==8;test_mute=false;test_layer_read=false;float gain=0;
  if(remote.state(1,1,&gain)!=1||std::abs(gain-std::pow(10.f,(test_potato?-6.f:-12.f)/20.f))>0.0001f||test_layer_read!=test_potato)return false;
  test_mute=true;if(remote.state(1,1,&gain)!=0||gain!=0)return false;
 }
 std::array<float,34> gains{};gains[10]=gains[11]=4.f;gains[18]=gains[19]=2.f;
 auto weights=normalized_weights((uint64_t(1)<<10)|(uint64_t(1)<<18),(uint64_t(1)<<11)|(uint64_t(1)<<19),gains);
 if(std::abs(weights[10]-2.f/3.f)>0.0001f||std::abs(weights[18]-1.f/3.f)>0.0001f||weights[0]!=0)return false;
 for(float level:{-1.f,-0.8f,0.f,0.8f,1.f})if(std::abs((weights[10]+weights[18])*level-level)>0.0001f)return false;
 if(channel_strip(6,22)!=3||channel_strip(14,22)!=4||channel_strip(10,34)!=5||channel_strip(26,34)!=7)return false;
 return true;
}
}
extern "C" int asio_run(int m,uint64_t ref_left,uint64_t ref_right,uint64_t ret,int initial,int seconds,int probe,int auto_mask,int auto_bus,int edition,int delay_ms,int hold_ms,int suppression,int allow_44100_resampling,int* final_mode){
 setvbuf(stdout,nullptr,_IONBF,0);
 struct HostWindow { HWND h; ~HostWindow(){if(h)DestroyWindow(h);} } hostWindow{
  CreateWindowExW(0,L"STATIC",L"VoiceMeeter AEC",0,0,0,0,0,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr)
 };
 struct InstanceGuard {HANDLE h;~InstanceGuard(){if(h)CloseHandle(h);}} instance{CreateMutexW(nullptr,FALSE,L"Local\\VoiceMeeterAECInsert")}; if(!instance.h||GetLastError()==ERROR_ALREADY_EXISTS){std::fprintf(stderr,"VoiceMeeter AEC is already running.\n");return 22;}
 reset_session_state(initial);
 if(edition!=1&&edition!=2){std::fprintf(stderr,"VoiceMeeter edition is invalid.\n");return 9;}
 const bool banana=edition==1;const wchar_t* driver_key=banana?L"SOFTWARE\\ASIO\\Voicemeeter Insert Virtual ASIO":L"SOFTWARE\\ASIO\\Voicemeeter Potato Insert Virtual ASIO";
 const char* edition_name=banana?"Banana":"Potato";const long expected_channels=banana?22:34;const int strip_count=banana?5:8;const int bus_count=banana?3:5;
 HKEY key=nullptr;
 if(RegOpenKeyExW(HKEY_LOCAL_MACHINE,driver_key,0,KEY_READ|KEY_WOW64_64KEY,&key)!=ERROR_SUCCESS){std::fprintf(stderr,"%s Insert x64 driver is missing.\n",edition_name);return 10;}
 wchar_t text[128]{};DWORD size=sizeof(text),type=0;
 auto rr=RegQueryValueExW(key,L"CLSID",nullptr,&type,reinterpret_cast<BYTE*>(text),&size);RegCloseKey(key);CLSID id{};
 if(rr!=ERROR_SUCCESS||type!=REG_SZ||FAILED(CLSIDFromString(text,&id)))return 11;
 if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 12;
 int result=0;bool created=false,started=false,owns_engine=false;Remote remote(strip_count);bool automatic=initial==3;
 do {
  if(FAILED(CoCreateInstance(id,nullptr,CLSCTX_INPROC_SERVER,id,reinterpret_cast<void**>(&driver)))){result=13;break;}
  if(!hostWindow.h||!driver->init(hostWindow.h)){result=14;break;}
  long ins=0,outs=0,min=0,max=0,preferred=0,gran=0,inlat=0,outlat=0;double rate=0;
  if(!ok(driver->getChannels(&ins,&outs),"channels")||!ok(driver->getSampleRate(&rate),"sample rate")||!ok(driver->getBufferSize(&min,&max,&preferred,&gran),"buffer")){result=15;break;}
  driver->getLatencies(&inlat,&outlat);
  std::printf("Insert %s: %ld inputs / %ld outputs; %.0f Hz; buffer %ld (min %ld max %ld).\nDriver-reported latency: input %ld, output %ld samples.\n",edition_name,ins,outs,rate,preferred,min,max,inlat,outlat);
  const long rate_hz=long(std::llround(rate));
  const bool rate_is_valid=std::isfinite(rate)&&std::abs(rate-double(rate_hz))<=0.5&&rate_hz>=8000&&rate_hz<=384000&&rate_hz%100==0;
  const bool needs_resampling=rate_hz!=48000;
  std::printf("config sample_rate=%ld resampling=%d\n",rate_hz,int(needs_resampling&&allow_44100_resampling));
  if(ins!=outs||ins!=expected_channels||preferred<1||preferred>8192||!rate_is_valid){result=16;break;}
  if(!probe&&needs_resampling&&(rate_hz!=44100||!allow_44100_resampling)){
   if(rate_hz==44100)std::fprintf(stderr,"VoiceMeeter is running at 44100 Hz. Enable 44.1 kHz compatibility resampling or switch VoiceMeeter to 48000 Hz.\n");
   else std::fprintf(stderr,"VoiceMeeter is running at %ld Hz. This build supports native 48000 Hz and optional 44100 Hz compatibility resampling.\n",rate_hz);
   result=16;break;
  }
  channels=ins;frames=preferred;formats.resize(channels);bool valid=true;
  for(long c=0;c<channels;c++){
   ASIOChannelInfo a{},b{};a.channel=b.channel=c;a.isInput=ASIOTrue;b.isInput=ASIOFalse;
   if(!ok(driver->getChannelInfo(&a),"input format")||!ok(driver->getChannelInfo(&b),"output format")){valid=false;break;}
   std::printf("%2ld : %-32.32s type %ld\n",c+1,a.name,long(a.type));
   if(bytes(a.type)==0||a.type!=b.type)valid=false;formats[c]=a.type;
  }
  if(!valid){std::fprintf(stderr,"Unsupported format; no stream opened.\n");result=17;break;}
  if(probe)break;
  if(m<0||m>=channels||ref_left==0||ref_right==0||((ref_left|ref_right|ret)>>channels)!=0||auto_mask<0||(auto_mask>>strip_count)!=0||auto_bus<0||auto_bus>=bus_count){result=18;break;}
  engine=aec_create(rate_hz,delay_ms,hold_ms,suppression);owns_engine=engine!=nullptr;
  if(!engine){std::fprintf(stderr,"Could not initialize AEC for %ld Hz.\n",rate_hz);result=18;break;}
  session_rate=rate_hz;mic=m;reference_left=ref_left;reference_right=ref_right;returns=ret;mode=initial==3?0:initial;
  if(needs_resampling)std::printf("Compatibility resampling active: 44100 Hz -> 48000 Hz AEC -> 44100 Hz.\n");
  initialize_weights();
  if(auto_mask>0&&!remote.open()){std::fprintf(stderr,"Remote API unavailable: Auto cannot read routing; Auto mode keeps AEC on.\n");if(automatic)mode=0;}
  bool reference_known=remote.reference_weights(auto_bus);
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
  auto begin=std::chrono::steady_clock::now(),last=begin,lastauto=begin,laststatus=begin;uint64_t previous=0;int laststate=-2;
  while(!quitting&&!fault){
   if(latency_dirty.exchange(false)){
    long newin=0,newout=0;
    if(ok(driver->getLatencies(&newin,&newout),"latency refresh"))std::printf("Updated driver latency: input %ld, output %ld samples.\n",newin,newout);
   }
   MSG msg;while(PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)){TranslateMessage(&msg);DispatchMessageW(&msg);}
   apply_control(read_control(),automatic,auto_mask,laststate);
   if(quitting)break;
   auto now=std::chrono::steady_clock::now();
   if(now-lastauto>=std::chrono::milliseconds(200)){
    reference_known=remote.reference_weights(auto_bus);
    if(automatic){
     int state=remote.state_mask(auto_mask,auto_bus);mode=state==0?1:0;
     if(state!=laststate)std::printf("Auto strips mask 0x%02x -> A%d : %s\n",auto_mask,auto_bus+1,state<0?"read unavailable, AEC on":state?"AEC on":"bypass");
     laststate=state;
    }
    lastauto=now;
   }
   if(now-laststatus>=std::chrono::milliseconds(100)){
    std::printf("status mode=%d auto=%d route_unknown=%d ref_route_unknown=%d mic=%.6f ref=%.6f ref_missing=%d dsp_failed=%d blocks=%llu\n",mode.load(),int(automatic),int(automatic&&laststate<0),int(!reference_known),micpeak.load(),refpeak.load(),int(missing.load()),int(dsp_failed.load()),callbacks.load());
    laststatus=now;
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
 if(started)driver->stop();if(created)driver->disposeBuffers();if(owns_engine){aec_destroy(engine);engine=nullptr;}if(driver){driver->Release();driver=nullptr;}
 SetConsoleCtrlHandler(ctrl,FALSE);CoUninitialize();std::puts("Closed. If the strip is silent, disable its PATCH INSERT.");return result;
}
extern "C" int asio_transport_test(){
 if(!reference_selftest())return 11;
 std::puts("Reference mix: Banana/Potato route gains, mutes, stereo balance and normalized headroom PASS.");
 if(combine_route_states(0,0)!=0||combine_route_states(0,1)!=1||combine_route_states(-1,0)!=-1||combine_route_states(-1,1)!=1||combine_route_states(1,-1)!=1)return 10;
 if(completion_code(true,4)!=0||completion_code(true,5)!=0||completion_code(false,4)!=34)return 8;
 reset_session_state(2);
 if(mode.load()!=2||callbacks.load()!=0||fault.load()!=0)return 5;
 if(message(kAsioLatenciesChanged,0,nullptr,nullptr)!=1||fault.load()!=0||!latency_dirty.load())return 9;
 if(message(kAsioResetRequest,0,nullptr,nullptr)!=1||fault.load()!=4)return 6;
 reset_session_state(1);rate_changed(44100.);if(fault.load()!=3||mode.load()!=1)return 7;
 reset_session_state(0);
 if(!route_active(1,0,0,0,0,0,false,0)||route_active(0,0,0,0,0,0,false,0)||route_active(1,1,0,0,0,0,false,0)||route_active(1,0,1,0,0,0,false,0)||route_active(1,0,0,-60,0,0,false,0)||route_active(1,0,0,0,0,-60,false,0)||route_active(1,0,0,0,0,0,true,0)||!route_active(1,0,0,0,0,0,true,1))return 4;
 for(auto t:{ASIOSTFloat32LSB,ASIOSTInt32LSB,ASIOSTInt24LSB,ASIOSTInt16LSB})for(long layout_channels:{22L,34L}){
  unsigned char b[32]{};int i=0;for(float v:{-1.f,-0.5f,0.f,0.123f,0.999f,1.f}){write_sample(b,i,t,v);if(std::abs(read_sample(b,i,t)-v)>0.00004f)return 1;i++;}
  channels=layout_channels;frames=192;returns=3;formats.assign(channels,t);buffers.assign(channels*2,{});clean.assign(192,0.25f);
  std::vector<std::vector<unsigned char>> data(size_t(channels)*4,std::vector<unsigned char>(192*bytes(t)));
  for(int c=0;c<channels*2;c++)for(int j=0;j<2;j++){buffers[c].buffers[j]=data[c*2+j].data();for(size_t k=0;k<data[c*2+j].size();k++)data[c*2+j][k]=static_cast<unsigned char>(c*7+k+j);}
  for(int j=0;j<2;j++){transfer(j);for(int c=2;c<channels;c++)if(data[c*2+j]!=data[(c+channels)*2+j])return 2;
   for(int c=0;c<2;c++)for(int k=0;k<192;k++)if(std::abs(read_sample(buffers[channels+c].buffers[j],k,t)-0.25f)>0.00004f)return 3;
  }
 }std::puts("Transport: Banana 22 + Potato 34 channels, double buffer, 4 formats, other channels unchanged: OK.");return 0;
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
 initialize_weights();
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
 if(std::abs(refpeak.load()-0.16f)>0.0001f)return 5;
 // Full-scale simultaneous references remain linear instead of hard-clipping.
 for(int c:{10,11,18,19})std::fill(data[c*2].begin(),data[c*2].end(),0.8f);
 process(0,ASIOTrue);
 for(int i=0;i<frames;i++)if(std::abs(leftdata[i]-0.8f)>0.0001f||std::abs(rightdata[i]-0.8f)>0.0001f)return 6;
 std::array<float,34> silent{};publish_weights(silent);
 for(int b=0;b<4;b++)process(0,ASIOTrue);
 for(int i=0;i<frames;i++)if(std::abs(leftdata[i])>0.00001f||std::abs(rightdata[i])>0.00001f)return 7;
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

