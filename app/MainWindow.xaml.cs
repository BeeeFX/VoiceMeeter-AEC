using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using Application = System.Windows.Application;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using RadioButton = System.Windows.Controls.RadioButton;
using Brush = System.Windows.Media.Brush;

namespace VoiceMeeterAEC;

public partial class MainWindow : Window
{
    private readonly string[] _arguments;
    private readonly AppSettings _settings;
    private readonly EngineHost _engine = new();
    private readonly Dictionary<int, RadioButton> _micButtons = [];
    private readonly Dictionary<int, CheckBox> _referenceButtons = [];
    private readonly Dictionary<int, RadioButton> _busButtons = [];
    private readonly Dictionary<int, CheckBox> _autoButtons = [];
    private WinForms.NotifyIcon? _tray;
    private Icon? _icon;
    private bool _ready;
    private bool _allowClose;
    private bool _startupPending;
    private DateTime _startupDeadline;
    private DispatcherTimer? _startupTimer;
    private readonly DispatcherTimer _showTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private UpdateRelease? _availableUpdate;
    private string _updateState = "idle";
    private int _updateProgress;
    private bool _updateBusy;
    private string UiLanguage => _settings.Language;

    private static readonly string[] StripNames = ["IN1", "IN2", "IN3", "IN4", "IN5", "VAIO", "AUX", "VAIO3"];
    private static readonly int[] StripStarts = [1, 3, 5, 7, 9, 11, 19, 27];
    private string?[] _stripLabels = new string?[8];

    private static readonly Dictionary<string, string> French = new()
    {
        ["Setup"] = "Configuration",
        ["Setup guide"] = "Guide de configuration",
        ["Advanced"] = "Avancé",
        ["Diagnostics"] = "Diagnostic",
        ["Match the columns already visible in VoiceMeeter."] = "Retrouvez les colonnes déjà visibles dans VoiceMeeter.",
        ["Follow the four steps once, then use Setup day to day."] = "Suivez ces quatre étapes une fois, puis utilisez Configuration au quotidien.",
        ["Fine-tune behaviour after the basic setup works."] = "Ajustez les options après avoir validé la configuration de base.",
        ["Three choices, then you’re ready"] = "Trois choix, puis tout est prêt",
        ["Each card is one VoiceMeeter column. No channel-number maths required."] = "Chaque carte représente une colonne VoiceMeeter. Aucun calcul de numéro de canal.",
        ["1  Where is your microphone?"] = "1  Où se trouve votre microphone ?",
        ["Pick its hardware input column."] = "Choisissez sa colonne d’entrée matérielle.",
        ["2  Which columns carry your speaker audio?"] = "2  Quelles colonnes contiennent le son de vos enceintes ?",
        ["AEC uses their audio as its reference—the speaker sound it should remove from your microphone. Select one or more."] = "L’AEC utilise leur son comme référence : c’est le son des enceintes qu’il doit retirer du microphone. Sélectionnez une ou plusieurs colonnes.",
        ["Do not select your microphone column, or AEC may suppress your voice."] = "Ne sélectionnez pas la colonne du microphone, sinon l’AEC risque d’atténuer votre voix.",
        ["3  Which A bus feeds your speakers?"] = "3  Quel bus A alimente vos enceintes ?",
        ["This tells Auto which speaker bus to watch. AEC turns on when a watched playback column is routed to this bus."] = "Ce choix indique à Auto quel bus d’enceintes surveiller. L’AEC s’active lorsqu’une colonne de lecture surveillée est envoyée vers ce bus.",
        ["Connect the cleaned microphone"] = "Connectez le microphone nettoyé",
        ["In PATCH INSERT, enable PRE-FX L and R only for the microphone strip. The in-app guide shows every step."] = "Dans PATCH INSERT, activez PRE-FX L et R uniquement pour la piste du microphone. Le guide intégré détaille chaque étape.",
        ["What the app connects"] = "Ce que l’application connecte",
        ["Your playback is the echo reference. The engine compares it with the raw mic and returns a cleaned mic signal."] = "Le son lu sert de référence d’écho. Le moteur le compare au micro brut et renvoie un signal micro nettoyé.",
        ["Speaker audio"] = "Son des enceintes",
        ["Clean mic"] = "Micro nettoyé",
        ["1  Prepare VoiceMeeter"] = "1  Préparez VoiceMeeter",
        ["Open VoiceMeeter Potato and confirm the sample rate is 48 kHz. Keep PATCH INSERT off while choosing settings."] = "Ouvrez VoiceMeeter Potato et vérifiez que la fréquence est de 48 kHz. Laissez PATCH INSERT désactivé pendant les réglages.",
        ["2  Match the three columns"] = "2  Associez les trois colonnes",
        ["Playback columns provide the audio reference AEC removes from your mic. The A bus is only the route Auto watches to decide when AEC should be active."] = "Les colonnes de lecture fournissent la référence audio que l’AEC retire du micro. Le bus A sert uniquement de route surveillée par Auto pour décider quand activer l’AEC.",
        ["Reference rule: include all speaker audio and exclude your microphone."] = "Règle de référence : incluez tout le son des enceintes et excluez votre microphone.",
        ["3  Start and verify"] = "3  Démarrez et vérifiez",
        ["Start echo cancellation. In Diagnostics, confirm 48 kHz and that audio blocks keep advancing before you connect the return."] = "Démarrez l’annulation d’écho. Dans Diagnostic, vérifiez 48 kHz et que les blocs audio progressent avant de connecter le retour.",
        ["4  Enable PATCH INSERT"] = "4  Activez PATCH INSERT",
        ["Open Menu → System Settings / Options → PATCH INSERT. Enable PRE-FX L and R only for your microphone strip, then test AEC, Bypass and Mute."] = "Ouvrez Menu → System Settings / Options → PATCH INSERT. Activez PRE-FX L et R uniquement pour la piste du micro, puis testez AEC, Bypass et Couper le micro.",
        ["Your microphone is {0} — enable the two highlighted boxes (Left and Right)."] = "Votre microphone est sur {0} — activez les deux cases surlignées (gauche et droite).",
        ["Before stopping the engine, switch those two PATCH INSERT buttons off."] = "Avant d’arrêter le moteur, désactivez ces deux boutons PATCH INSERT.",
        ["Behaviour"] = "Comportement",
        ["Echo suppression"] = "Suppression de l’écho",
        ["Gentle preserves your voice best."] = "Douce préserve le mieux votre voix.",
        ["Mode when the app starts"] = "Mode au démarrage de l’application",
        ["Auto is recommended for everyday use."] = "Auto est recommandé au quotidien.",
        ["Microphone side"] = "Côté du microphone",
        ["Most mono microphones arrive on the left."] = "La plupart des micros mono arrivent à gauche.",
        ["Engine settings apply the next time it starts."] = "Les réglages du moteur s'appliquent à son prochain démarrage.",
        ["Auto mode"] = "Mode Auto",
        ["Watch these VoiceMeeter columns. AEC turns on when any selected column is routed to your speaker bus."] = "Surveillez ces colonnes VoiceMeeter. L’AEC s’active dès qu’une colonne choisie est envoyée vers le bus des enceintes.",
        ["Watch every playback column routed to the selected A bus"] = "Surveiller toutes les colonnes de lecture envoyées vers le bus A choisi",
        ["Timing"] = "Synchronisation",
        ["Leave both at zero unless you are correcting a measured alignment problem."] = "Laissez les deux valeurs à zéro sauf pour corriger un décalage mesuré.",
        ["Microphone hold"] = "Retard réel du microphone",
        ["AEC delay estimate"] = "Estimation du délai AEC",
        ["Start VoiceMeeter AEC when I sign in to Windows"] = "Démarrer VoiceMeeter AEC à l’ouverture de ma session Windows",
        ["The app starts quietly in the system tray and waits for VoiceMeeter."] = "L’application démarre discrètement dans la zone de notification et attend VoiceMeeter.",
        ["Updates"] = "Mises à jour",
        ["Current version {0}"] = "Version actuelle {0}",
        ["Current version {0} · You’re up to date."] = "Version actuelle {0} · Vous êtes à jour.",
        ["Check for updates automatically"] = "Rechercher automatiquement les mises à jour",
        ["Checks GitHub Releases once a day. Installation always asks first."] = "Vérifie GitHub Releases une fois par jour. L’installation demande toujours votre accord.",
        ["Check for updates"] = "Rechercher des mises à jour",
        ["Checking for updates…"] = "Recherche de mises à jour…",
        ["Version {0} is available."] = "La version {0} est disponible.",
        ["Update available"] = "Mise à jour disponible",
        ["Download and install"] = "Télécharger et installer",
        ["Downloading update… {0}%"] = "Téléchargement de la mise à jour… {0} %",
        ["Preparing update…"] = "Préparation de la mise à jour…",
        ["Could not check for updates."] = "Impossible de rechercher les mises à jour.",
        ["Update installation failed. Your current version is still installed."] = "Échec de l’installation. Votre version actuelle reste installée.",
        ["Retry update"] = "Réessayer la mise à jour",
        ["VoiceMeeter AEC will close, install version {0}, and reopen. Echo cancellation will be interrupted briefly. Continue?"] = "VoiceMeeter AEC va se fermer, installer la version {0}, puis se rouvrir. L’annulation d’écho sera brièvement interrompue. Continuer ?",
        ["Install update"] = "Installer la mise à jour",
        ["The update could not be installed."] = "La mise à jour n’a pas pu être installée.",
        ["Technical activity from the audio engine. Useful when setup is not working."] = "Activité technique du moteur audio, utile lorsqu’un réglage ne fonctionne pas.",
        ["Settings saved automatically"] = "Réglages enregistrés automatiquement",
        ["Start echo cancellation"] = "Démarrer l’annulation d’écho",
        ["Stop engine"] = "Arrêter le moteur",
        ["AEC on"] = "AEC actif",
        ["Mute"] = "Couper le micro",
        ["Ready"] = "Prêt",
        ["Starting…"] = "Démarrage…",
        ["Running"] = "En marche",
        ["Auto active"] = "Auto actif",
        ["AEC active"] = "AEC actif",
        ["Bypass active"] = "Bypass actif",
        ["Microphone muted"] = "Micro coupé",
        ["Reconnecting…"] = "Reconnexion…",
        ["Stopped"] = "Arrêté",
        ["Show VoiceMeeter AEC"] = "Afficher VoiceMeeter AEC",
        ["Exit"] = "Quitter",
        ["Gentle"] = "Douce",
        ["Balanced"] = "Équilibrée",
        ["Strong"] = "Forte",
        ["AEC always on"] = "AEC toujours actif",
        ["Auto (recommended)"] = "Auto (recommandé)",
        ["Mute microphone"] = "Couper le microphone",
        ["Left"] = "Gauche",
        ["Right"] = "Droite",
        ["Hardware input"] = "Entrée matérielle",
        ["Virtual input"] = "Entrée virtuelle",
        ["channels"] = "canaux",
        ["Please start VoiceMeeter Potato first, then try again."] = "Démarrez d’abord VoiceMeeter Potato, puis réessayez.",
        ["The audio engine stopped. Open Diagnostics for details."] = "Le moteur audio s’est arrêté. Ouvrez Diagnostic pour plus de détails.",
        ["Before stopping, disable the microphone’s PATCH INSERT return in VoiceMeeter. Continue?"] = "Avant l’arrêt, désactivez le retour PATCH INSERT du microphone dans VoiceMeeter. Continuer ?",
        ["The engine did not stop yet. Open Diagnostics and try again."] = "Le moteur ne s’est pas encore arrêté. Ouvrez Diagnostic et réessayez.",
        ["Could not save settings"] = "Impossible d’enregistrer les réglages",
        ["Could not start"] = "Démarrage impossible",
        ["Light mode"] = "Mode clair",
        ["Dark mode"] = "Mode sombre",
        ["Language"] = "Langue"
    };

    private static readonly Dictionary<string, string> DarkTheme = new()
    {
        ["WindowBackground"] = "#0A1119", ["SidebarBackground"] = "#0D1925", ["SurfaceBackground"] = "#0F1823",
        ["CardBackground"] = "#131F2B", ["CardBorder"] = "#253647", ["ControlBackground"] = "#192837",
        ["ControlHover"] = "#223548", ["DisabledBackground"] = "#151F2A", ["Ink"] = "#F1F6F8",
        ["MutedInk"] = "#97A9B9", ["SubtleInk"] = "#6F8598", ["Accent"] = "#20C5C7",
        ["AccentHover"] = "#2AD9D8", ["AccentPressed"] = "#16989D", ["AccentSoft"] = "#123B42",
        ["AccentText"] = "#9CEAED", ["NavText"] = "#A9BBC9", ["NavHover"] = "#152A3A",
        ["NavActive"] = "#183A4B", ["InfoBackground"] = "#102F36", ["InfoText"] = "#A0DEE2",
        ["WarningBackground"] = "#352918", ["WarningText"] = "#F0C77A", ["StatusBackground"] = "#182735",
        ["DiagnosticsBackground"] = "#071018", ["DiagnosticsText"] = "#CBE3E5", ["PrimaryText"] = "#061517"
    };

    private static readonly Dictionary<string, string> LightTheme = new()
    {
        ["WindowBackground"] = "#F3F6F8", ["SidebarBackground"] = "#102A3D", ["SurfaceBackground"] = "#FFFFFF",
        ["CardBackground"] = "#FFFFFF", ["CardBorder"] = "#D8E3EA", ["ControlBackground"] = "#EEF3F6",
        ["ControlHover"] = "#E1EAEE", ["DisabledBackground"] = "#EDF1F3", ["Ink"] = "#14263B",
        ["MutedInk"] = "#60748A", ["SubtleInk"] = "#7890A3", ["Accent"] = "#07868F",
        ["AccentHover"] = "#069AA5", ["AccentPressed"] = "#056A73", ["AccentSoft"] = "#DFF4F4",
        ["AccentText"] = "#075F68", ["NavText"] = "#B8C8D8", ["NavHover"] = "#19384F",
        ["NavActive"] = "#1B4358", ["InfoBackground"] = "#E6F5F5", ["InfoText"] = "#315E65",
        ["WarningBackground"] = "#FFF4E2", ["WarningText"] = "#805719", ["StatusBackground"] = "#EDF3F6",
        ["DiagnosticsBackground"] = "#102A3D", ["DiagnosticsText"] = "#D8E7EF", ["PrimaryText"] = "#FFFFFF"
    };

    public MainWindow(string[] arguments)
    {
        _arguments = arguments;
        _settings = _arguments.Contains("--preview") || _arguments.Contains("--check-ui")
            ? new AppSettings()
            : AppSettings.Load();
        if (_arguments.Contains("--dark")) _settings.Theme = "dark";
        if (_arguments.Contains("--light")) _settings.Theme = "light";
        InitializeComponent();
        ApplyTheme();
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        BuildSelectors();
        ApplySettingsToControls();
        ApplyLanguage();
        ConfigureEngineEvents();
        ConfigureTray();
        _showTimer.Tick += (_, _) => { if (App.ShowSignal?.WaitOne(0) == true) ShowWindow(); };
        _showTimer.Start();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveSettingsNow(); };
        Loaded += Window_Loaded;
        Closing += Window_Closing;
        _ready = true;
    }

    private string T(string english) => UiLanguage == "fr" && French.TryGetValue(english, out var value) ? value : english;

    private void ApplyTheme()
    {
        var palette = _settings.Theme == "light" ? LightTheme : DarkTheme;
        foreach (var pair in palette)
            Application.Current.Resources[pair.Key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(pair.Value));
        if (ThemeButton is not null)
            ThemeButton.Content = _settings.Theme == "dark" ? "☀  " + T("Light mode") : "☾  " + T("Dark mode");
        ApplyTitleBarTheme();
    }

    private void ApplyTitleBarTheme()
    {
        if (!IsInitialized) return;
        try
        {
            var dark = _settings.Theme == "dark" ? 1 : 0;
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void BuildSelectors()
    {
        _micButtons.Clear();
        MicStripPanel.Children.Clear();
        for (var strip = 1; strip <= 5; strip++)
        {
            var button = CreateStripCard(strip, "mic");
            _micButtons.Add(strip, button);
            MicStripPanel.Children.Add(button);
        }

        _referenceButtons.Clear();
        ReferenceStripPanel.Children.Clear();
        for (var strip = 1; strip <= 8; strip++)
        {
            var button = CreateReferenceCard(strip);
            _referenceButtons.Add(strip, button);
            ReferenceStripPanel.Children.Add(button);
        }

        _busButtons.Clear();
        BusPanel.Children.Clear();
        for (var bus = 1; bus <= 5; bus++)
        {
            var button = new RadioButton
            {
                Content = $"A{bus}",
                Tag = bus,
                GroupName = "bus",
                Style = (Style)FindResource("PillRadio")
            };
            button.Checked += SelectorChanged;
            _busButtons.Add(bus, button);
            BusPanel.Children.Add(button);
        }

        _autoButtons.Clear();
        AutoStripPanel.Children.Clear();
        for (var strip = 1; strip <= 8; strip++)
        {
            var button = new CheckBox
            {
                Content = StripNames[strip - 1],
                Tag = strip,
                ToolTip = $"{T(strip <= 5 ? "Hardware input" : "Virtual input")} · {T("channels")} {StripStarts[strip - 1]},{StripStarts[strip - 1] + 1}",
                Style = (Style)FindResource("PillCheck")
            };
            button.Checked += SelectorChanged;
            button.Unchecked += SelectorChanged;
            _autoButtons.Add(strip, button);
            AutoStripPanel.Children.Add(button);
        }
    }

    private RadioButton CreateStripCard(int strip, string group)
    {
        var button = new RadioButton
        {
            Content = CreateStripContent(strip),
            Tag = strip,
            GroupName = group,
            Style = (Style)FindResource("ChoiceCard")
        };
        button.Checked += SelectorChanged;
        return button;
    }

    private CheckBox CreateReferenceCard(int strip)
    {
        var button = new CheckBox
        {
            Content = CreateStripContent(strip),
            Tag = strip,
            Style = (Style)FindResource("ChoiceCheck")
        };
        button.Checked += SelectorChanged;
        button.Unchecked += SelectorChanged;
        return button;
    }

    private StackPanel CreateStripContent(int strip)
    {
        var start = StripStarts[strip - 1];
        var type = strip <= 5 ? T("Hardware input") : T("Virtual input");
        var content = new StackPanel();
        var name = new TextBlock { Text = StripNames[strip - 1], FontSize = 16, FontWeight = FontWeights.SemiBold };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var label = new TextBlock { Text = _stripLabels[strip - 1] ?? type, Margin = new Thickness(0, 3, 0, 0), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = _stripLabels[strip - 1] };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        var channels = new TextBlock { Text = $"{T("channels")} {start}–{start + 1}", Margin = new Thickness(0, 2, 0, 0), FontSize = 11 };
        channels.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        content.Children.Add(name);
        content.Children.Add(label);
        content.Children.Add(channels);
        return content;
    }

    private void ApplySettingsToControls()
    {
        _micButtons[_settings.MicrophoneStrip].IsChecked = true;
        foreach (var pair in _referenceButtons) pair.Value.IsChecked = _settings.ReferenceStrips.Contains(pair.Key);
        _busButtons[_settings.SpeakerBus].IsChecked = true;
        foreach (var pair in _autoButtons) pair.Value.IsChecked = _settings.AutoStrips.Contains(pair.Key);
        WatchAllBox.IsChecked = _settings.WatchAllStrips;
        HoldSlider.Value = _settings.HoldMs;
        DelaySlider.Value = _settings.DelayMs;
        StartupBox.IsChecked = _settings.StartWithWindows;
        AutomaticUpdatesBox.IsChecked = _settings.CheckForUpdatesAutomatically;
        SelectComboByTag(MicSideBox, _settings.MicrophoneSide);
        SelectComboByTag(SuppressionBox, _settings.Suppression);
        SelectComboByTag(StartModeBox, _settings.StartMode);
        LanguageBox.SelectedIndex = _settings.Language == "fr" ? 1 : 0;
        UpdateReferenceAvailability(_settings.MicrophoneStrip);
        UpdatePatchDiagram(_settings.MicrophoneStrip);
        UpdateAutoControls();
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
            if (Equals(item.Tag, tag)) { combo.SelectedItem = item; return; }
    }

    private void SyncSettingsFromControls()
    {
        _settings.MicrophoneStrip = SelectedKey(_micButtons, 1);
        _settings.ReferenceStrips = SelectedKeys(_referenceButtons);
        if (_settings.ReferenceStrips.Count == 0) _settings.ReferenceStrips.Add(6);
        _settings.SpeakerBus = SelectedKey(_busButtons, 2);
        _settings.AutoStrips = _autoButtons.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToList();
        if (_settings.AutoStrips.Count == 0) _settings.AutoStrips.AddRange(_settings.ReferenceStrips);
        _settings.WatchAllStrips = WatchAllBox.IsChecked == true;
        _settings.MicrophoneSide = SelectedTag(MicSideBox, "left");
        _settings.Suppression = SelectedTag(SuppressionBox, "gentle");
        _settings.StartMode = SelectedTag(StartModeBox, "auto");
        _settings.HoldMs = (int)Math.Round(HoldSlider.Value);
        _settings.DelayMs = (int)Math.Round(DelaySlider.Value);
        _settings.StartWithWindows = StartupBox.IsChecked == true;
        _settings.CheckForUpdatesAutomatically = AutomaticUpdatesBox.IsChecked == true;
    }

    private static int SelectedKey(Dictionary<int, RadioButton> buttons, int fallback) =>
        buttons.FirstOrDefault(pair => pair.Value.IsChecked == true).Key is var value && value != 0 ? value : fallback;

    private static List<int> SelectedKeys(Dictionary<int, CheckBox> buttons) =>
        buttons.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).Order().ToList();

    private static string SelectedTag(ComboBox combo, string fallback) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private void QueueSave()
    {
        if (!_ready || _arguments.Contains("--preview") || _arguments.Contains("--check-ui")) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveSettingsNow()
    {
        if (!_ready || _arguments.Contains("--preview") || _arguments.Contains("--check-ui")) return;
        _saveTimer.Stop();
        SyncSettingsFromControls();
        try
        {
            _settings.Save(Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName);
            UnsavedText.Text = T("Settings saved automatically");
        }
        catch (Exception exception)
        {
            UnsavedText.Text = T("Could not save settings");
            DiagnosticsBox.Text += Environment.NewLine + exception;
        }
    }

    private void SelectorChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is RadioButton micButton && micButton.GroupName == "mic" && micButton.Tag is int micStrip)
        {
            if (_referenceButtons[micStrip].IsChecked == true)
            {
                _referenceButtons[6].IsChecked = true;
                _referenceButtons[micStrip].IsChecked = false;
            }
            if (!SelectedKeys(_referenceButtons).Any()) _referenceButtons[6].IsChecked = true;
            UpdateReferenceAvailability(micStrip);
            UpdatePatchDiagram(micStrip);
        }
        if (sender is CheckBox referenceButton && referenceButton.Tag is int strip && _referenceButtons.TryGetValue(strip, out var knownReference) && ReferenceEquals(referenceButton, knownReference))
        {
            if (!SelectedKeys(_referenceButtons).Any())
            {
                referenceButton.IsChecked = true;
                return;
            }
            if (referenceButton.IsChecked == true) _autoButtons[strip].IsChecked = true;
        }
        UpdateAutoControls();
        QueueSave();
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        UpdateAutoControls();
        QueueSave();
    }

    private void TimingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (HoldValue is null || DelayValue is null) return;
        HoldValue.Text = $"{Math.Round(HoldSlider.Value)} ms";
        DelayValue.Text = $"{Math.Round(DelaySlider.Value)} ms";
        QueueSave();
    }

    private void UpdateAutoControls()
    {
        var enabled = WatchAllBox.IsChecked != true;
        foreach (var button in _autoButtons.Values) button.IsEnabled = enabled;
    }

    private void UpdateReferenceAvailability(int microphoneStrip)
    {
        foreach (var pair in _referenceButtons) pair.Value.IsEnabled = pair.Key != microphoneStrip;
    }

    private void UpdatePatchDiagram(int microphoneStrip)
    {
        var left = 25d + (Math.Clamp(microphoneStrip, 1, 5) - 1) * 48d;
        Canvas.SetLeft(PatchLeftBox, left);
        Canvas.SetLeft(PatchRightBox, left + 24d);
        PatchWhereText.Text = string.Format(T("Your microphone is {0} — enable the two highlighted boxes (Left and Right)."), $"IN{microphoneStrip}");
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is not ComboBoxItem item) return;
        _settings.Language = item.Tag?.ToString() == "fr" ? "fr" : "en";
        if (!_ready) return;
        var mic = SelectedKey(_micButtons, 1);
        var references = SelectedKeys(_referenceButtons);
        var bus = SelectedKey(_busButtons, 2);
        var auto = _autoButtons.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToList();
        BuildSelectors();
        _micButtons[mic].IsChecked = true;
        foreach (var pair in _referenceButtons) pair.Value.IsChecked = references.Contains(pair.Key);
        _busButtons[bus].IsChecked = true;
        foreach (var pair in _autoButtons) pair.Value.IsChecked = auto.Contains(pair.Key);
        ApplyLanguage();
        ConfigureTray();
        QueueSave();
    }

    private void ApplyLanguage()
    {
        SetupNavText.Text = T("Setup");
        AdvancedNavText.Text = T("Advanced");
        DiagnosticsNavText.Text = T("Diagnostics");
        GuideNavText.Text = T("Setup guide");
        LanguageLabel.Text = T("Language");
        ApplyTheme();
        var page = SetupView.Visibility == Visibility.Visible ? "setup" : GuideView.Visibility == Visibility.Visible ? "guide" : AdvancedView.Visibility == Visibility.Visible ? "advanced" : "diagnostics";
        ShowPage(page);
        MicHeading.Text = T("1  Where is your microphone?");
        MicHelp.Text = T("Pick its hardware input column.");
        ReferenceHeading.Text = T("2  Which columns carry your speaker audio?");
        ReferenceHelp.Text = T("AEC uses their audio as its reference—the speaker sound it should remove from your microphone. Select one or more.");
        ReferenceWarning.Text = T("Do not select your microphone column, or AEC may suppress your voice.");
        BusHeading.Text = T("3  Which A bus feeds your speakers?");
        BusHelp.Text = T("This tells Auto which speaker bus to watch. AEC turns on when a watched playback column is routed to this bus.");
        PatchHeading.Text = T("Connect the cleaned microphone");
        PatchText.Text = T("In PATCH INSERT, enable PRE-FX L and R only for the microphone strip. The in-app guide shows every step.");
        GuideFlowHeading.Text = T("What the app connects");
        GuideFlowText.Text = T("Your playback is the echo reference. The engine compares it with the raw mic and returns a cleaned mic signal.");
        FlowPlayback.Text = T("Speaker audio");
        FlowCleanMic.Text = T("Clean mic");
        GuideStep1Heading.Text = T("1  Prepare VoiceMeeter");
        GuideStep1Text.Text = T("Open VoiceMeeter Potato and confirm the sample rate is 48 kHz. Keep PATCH INSERT off while choosing settings.");
        GuideStep2Heading.Text = T("2  Match the three columns");
        GuideStep2Text.Text = T("Playback columns provide the audio reference AEC removes from your mic. The A bus is only the route Auto watches to decide when AEC should be active.");
        GuideReferenceRule.Text = T("Reference rule: include all speaker audio and exclude your microphone.");
        GuideStep3Heading.Text = T("3  Start and verify");
        GuideStep3Text.Text = T("Start echo cancellation. In Diagnostics, confirm 48 kHz and that audio blocks keep advancing before you connect the return.");
        GuideStep4Heading.Text = T("4  Enable PATCH INSERT");
        GuideStep4Text.Text = T("Open Menu → System Settings / Options → PATCH INSERT. Enable PRE-FX L and R only for your microphone strip, then test AEC, Bypass and Mute.");
        UpdatePatchDiagram(SelectedKey(_micButtons, 1));
        GuideStopRule.Text = T("Before stopping the engine, switch those two PATCH INSERT buttons off.");
        BehaviourHeading.Text = T("Behaviour");
        SuppressionLabel.Text = T("Echo suppression");
        SuppressionHelp.Text = T("Gentle preserves your voice best.");
        StartModeLabel.Text = T("Mode when the app starts");
        StartModeHelp.Text = T("Auto is recommended for everyday use.");
        MicSideLabel.Text = T("Microphone side");
        MicSideHelp.Text = T("Most mono microphones arrive on the left.");
        RestartNote.Text = T("Engine settings apply the next time it starts.");
        AutoHeading.Text = T("Auto mode");
        AutoHelp.Text = T("Watch these VoiceMeeter columns. AEC turns on when any selected column is routed to your speaker bus.");
        WatchAllBox.Content = T("Watch every playback column routed to the selected A bus");
        TimingHeading.Text = T("Timing");
        TimingHelp.Text = T("Leave both at zero unless you are correcting a measured alignment problem.");
        HoldLabel.Text = T("Microphone hold");
        DelayLabel.Text = T("AEC delay estimate");
        StartupBox.Content = T("Start VoiceMeeter AEC when I sign in to Windows");
        StartupHelp.Text = T("The app starts quietly in the system tray and waits for VoiceMeeter.");
        UpdatesHeading.Text = T("Updates");
        AutomaticUpdatesBox.Content = T("Check for updates automatically");
        AutomaticUpdatesHelp.Text = T("Checks GitHub Releases once a day. Installation always asks first.");
        RefreshUpdateText();
        DiagnosticsHelp.Text = T("Technical activity from the audio engine. Useful when setup is not working.");
        UnsavedText.Text = T("Settings saved automatically");
        EngineButton.Content = _engine.Alive ? T("Stop engine") : T("Start echo cancellation");
        AutoModeButton.Content = "Auto";
        AecModeButton.Content = T("AEC on");
        BypassModeButton.Content = "Bypass";
        MuteModeButton.Content = T("Mute");
        SetComboLabels();
        UpdateStatusVisual(_engine.Status);
    }

    private void SetComboLabels()
    {
        var suppression = new Dictionary<string, string> { ["gentle"] = T("Gentle"), ["balanced"] = T("Balanced"), ["strong"] = T("Strong") };
        foreach (ComboBoxItem item in SuppressionBox.Items) item.Content = suppression[item.Tag!.ToString()!];
        var modes = new Dictionary<string, string> { ["bypass"] = "Bypass", ["aec"] = T("AEC always on"), ["auto"] = T("Auto (recommended)"), ["mute"] = T("Mute microphone") };
        foreach (ComboBoxItem item in StartModeBox.Items) item.Content = modes[item.Tag!.ToString()!];
        foreach (ComboBoxItem item in MicSideBox.Items) item.Content = T(item.Tag!.ToString() == "left" ? "Left" : "Right");
    }

    private void ConfigureEngineEvents()
    {
        _engine.StatusChanged += status => Dispatcher.Invoke(() =>
        {
            UpdateStatusVisual(status);
            if (_startupPending && status is ("running" or "auto" or "aec" or "bypass" or "mute"))
            {
                _startupPending = false;
                _startupTimer?.Stop();
            }
        });
        _engine.DiagnosticsChanged += () => Dispatcher.Invoke(() => DiagnosticsBox.Text = _engine.Diagnostics);
        _engine.Exited += code => Dispatcher.Invoke(() =>
        {
            EngineButton.Content = T("Start echo cancellation");
            SetModeButtons(false);
            if (_startupPending && !_engine.EverRunning && code is 13 or 14 or 20 or 35 && DateTime.Now < _startupDeadline)
                return;
            if (code != 0)
            {
                _startupPending = false;
                _startupTimer?.Stop();
                if (IsVisible)
                {
                    ShowPage("diagnostics");
                    System.Windows.MessageBox.Show(this, T("The audio engine stopped. Open Diagnostics for details."), "VoiceMeeter AEC", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                    _tray?.ShowBalloonTip(5000, "VoiceMeeter AEC", T("The audio engine stopped. Open Diagnostics for details."), WinForms.ToolTipIcon.Warning);
            }
        });
    }

    private void ConfigureTray()
    {
        if (_arguments.Contains("--preview") || _arguments.Contains("--check-ui")) return;
        if (_tray is null)
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            _icon = resource is null ? SystemIcons.Application : new Icon(resource.Stream);
            _tray = new WinForms.NotifyIcon { Icon = _icon, Visible = true, Text = "VoiceMeeter AEC" };
            _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
            _tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(ShowUpdates);
        }
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(T("Show VoiceMeeter AEC"), null, (_, _) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Auto", null, (_, _) => Dispatcher.Invoke(() => SetMode('t')));
        menu.Items.Add(T("AEC on"), null, (_, _) => Dispatcher.Invoke(() => SetMode('a')));
        menu.Items.Add("Bypass", null, (_, _) => Dispatcher.Invoke(() => SetMode('b')));
        menu.Items.Add(T("Mute"), null, (_, _) => Dispatcher.Invoke(() => SetMode('m')));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(T("Diagnostics"), null, (_, _) => Dispatcher.Invoke(() => { ShowWindow(); ShowPage("diagnostics"); }));
        menu.Items.Add(T("Exit"), null, async (_, _) => await Dispatcher.InvokeAsync(ExitApplication));
        _tray.ContextMenuStrip?.Dispose();
        _tray.ContextMenuStrip = menu;
    }

    private void UpdateStatusVisual(string status)
    {
        var (text, color) = status switch
        {
            "starting" => (T("Starting…"), "#D18B20"),
            "running" => (T("Running"), "#18A67E"),
            "auto" => (T("Auto active"), "#18A67E"),
            "aec" => (T("AEC active"), "#18A67E"),
            "bypass" => (T("Bypass active"), "#D18B20"),
            "mute" => (T("Microphone muted"), "#E05858"),
            "reconnecting" => (T("Reconnecting…"), "#D18B20"),
            "stopped" => (T("Stopped"), "#90A0AE"),
            _ => (T("Ready"), "#90A0AE")
        };
        StatusText.Text = text;
        StatusDot.Fill = (Brush)new BrushConverter().ConvertFromString(color)!;
        if (_tray is not null) _tray.Text = ("VoiceMeeter AEC · " + text)[..Math.Min(63, ("VoiceMeeter AEC · " + text).Length)];
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_arguments.Contains("--with-labels")) LoadVoiceMeeterLabels();
        if (_arguments.Contains("--check-ui"))
        {
            ValidateUiMappings();
            _allowClose = true;
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        var previewIndex = Array.IndexOf(_arguments, "--preview");
        if (previewIndex >= 0 && previewIndex + 1 < _arguments.Length)
        {
            var pageIndex = Array.IndexOf(_arguments, "--preview-page");
            if (pageIndex >= 0 && pageIndex + 1 < _arguments.Length)
                ShowPage(_arguments[pageIndex + 1]);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (_arguments.Contains("--preview-bottom"))
            {
                if (GuideView.Visibility == Visibility.Visible) GuideView.ScrollToEnd();
                else if (SetupView.Visibility == Visibility.Visible) SetupView.ScrollToEnd();
                else if (AdvancedView.Visibility == Visibility.Visible) AdvancedView.ScrollToEnd();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            SavePreview(_arguments[previewIndex + 1]);
            _allowClose = true;
            Close();
            Application.Current.Shutdown(0);
            return;
        }

        if (_arguments.Contains("--startup"))
        {
            Hide();
            BeginStartupWait();
        }
        else
        {
            LoadVoiceMeeterLabels();
            if (_arguments.Contains("--restart-engine")) StartEngine(true);
        }

        _ = Task.Run(UpdateService.CleanupOldUpdateFiles);
        if (_settings.CheckForUpdatesAutomatically &&
            (DateTime.UtcNow - _settings.LastUpdateCheckUtc) >= TimeSpan.FromDays(1))
            await CheckForUpdatesAsync(false);
    }

    private void LoadVoiceMeeterLabels()
    {
        var labels = VoiceMeeterLabels.TryRead();
        if (labels.All(string.IsNullOrWhiteSpace)) return;
        var mic = SelectedKey(_micButtons, 1);
        var references = SelectedKeys(_referenceButtons);
        var bus = SelectedKey(_busButtons, 2);
        var auto = _autoButtons.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToList();
        var wasReady = _ready;
        _ready = false;
        try
        {
            _stripLabels = labels;
            BuildSelectors();
            _micButtons[mic].IsChecked = true;
            foreach (var pair in _referenceButtons) pair.Value.IsChecked = references.Contains(pair.Key);
            _busButtons[bus].IsChecked = true;
            foreach (var pair in _autoButtons) pair.Value.IsChecked = auto.Contains(pair.Key);
            UpdateReferenceAvailability(mic);
            UpdatePatchDiagram(mic);
        }
        finally
        {
            _ready = wasReady;
        }
    }

    private void ValidateUiMappings()
    {
        UpdateService.RunSelfTests();
        if (_micButtons.Count != 5 || _referenceButtons.Count != 8 || _busButtons.Count != 5 || _autoButtons.Count != 8)
            throw new InvalidOperationException("The mixer selectors were not created correctly.");
        if (StripStarts[5] != 11 || StripStarts[6] != 19 || StripStarts[7] != 27)
            throw new InvalidOperationException("Virtual input channel mapping is invalid.");
        _micButtons[3].IsChecked = true;
        if (Math.Abs(Canvas.GetLeft(PatchLeftBox) - 121d) > 0.1 || Math.Abs(Canvas.GetLeft(PatchRightBox) - 145d) > 0.1)
            throw new InvalidOperationException("The PATCH INSERT highlight did not follow the microphone strip.");
        _referenceButtons[6].IsChecked = true;
        _referenceButtons[7].IsChecked = true;
        foreach (var pair in _referenceButtons.Where(pair => pair.Key is not (6 or 7))) pair.Value.IsChecked = false;
        _busButtons[4].IsChecked = true;
        SelectComboByTag(MicSideBox, "right");
        var arguments = BuildEngineArguments();
        var joined = string.Join(' ', arguments);
        if (!joined.Contains("--mic 6") || !joined.Contains("--ref 11,12,19,20") || !joined.Contains("--returns 5,6") || !joined.Contains("--auto-bus 4"))
            throw new InvalidOperationException("A visible strip selection produced incorrect engine channels.");
        var migrated = AppSettings.Parse("""{"schema":1,"language":"fr","mic":2,"refL":19,"strips":"6,7,8","autoScope":0,"bus":3,"mode":2,"suppression":"balanced","hold":7,"delay":9}""", true);
        if (!migrated.MigratedFromLegacy || migrated.Language != "fr" || migrated.MicrophoneStrip != 1 || migrated.MicrophoneSide != "right" || migrated.ReferenceStrip != 7 || migrated.SpeakerBus != 3 || migrated.StartMode != "auto" || migrated.Suppression != "balanced" || !migrated.StartWithWindows)
            throw new InvalidOperationException("Legacy launcher settings were not migrated correctly.");
        if (migrated.Theme != "dark")
            throw new InvalidOperationException("Legacy settings did not receive the default dark theme.");
        var upgraded = AppSettings.Parse("""{"Schema":2,"ReferenceStrip":7}""", false);
        var multiple = AppSettings.Parse("""{"Schema":2,"ReferenceStrips":[6,7]}""", false);
        var updatesDisabled = AppSettings.Parse("""{"Schema":2,"CheckForUpdatesAutomatically":false}""", false);
        if (!upgraded.ReferenceStrips.SequenceEqual([7]) || !multiple.ReferenceStrips.SequenceEqual([6, 7]) ||
            !upgraded.CheckForUpdatesAutomatically || updatesDisabled.CheckForUpdatesAutomatically)
            throw new InvalidOperationException("Saved settings migration is invalid.");
        foreach (var language in new[] { "en", "fr" })
        {
            _settings.Language = language;
            ApplyLanguage();
            if (string.IsNullOrWhiteSpace(HeaderTitle.Text) || string.IsNullOrWhiteSpace(EngineButton.Content?.ToString()))
                throw new InvalidOperationException("A localized UI label is missing.");
        }
        foreach (var theme in new[] { "dark", "light" })
        {
            _settings.Theme = theme;
            ApplyTheme();
            if (Application.Current.Resources["WindowBackground"] is not SolidColorBrush)
                throw new InvalidOperationException("A theme resource is missing.");
        }
        ShowPage("guide");
        if (GuideView.Visibility != Visibility.Visible || string.IsNullOrWhiteSpace(GuideStep4Text.Text))
            throw new InvalidOperationException("The integrated setup guide is incomplete.");
        ShowPage("setup");
    }

    private void SavePreview(string path)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void RefreshUpdateText()
    {
        if (UpdateStatusText is null || UpdateButton is null || UpdateAvailableButton is null) return;
        var current = UpdateService.CurrentVersionText;
        UpdateStatusText.Text = _updateState switch
        {
            "checking" => T("Checking for updates…"),
            "current" => string.Format(T("Current version {0} · You’re up to date."), current),
            "available" when _availableUpdate is not null => string.Format(T("Version {0} is available."), _availableUpdate.VersionText),
            "downloading" => string.Format(T("Downloading update… {0}%"), _updateProgress),
            "preparing" => T("Preparing update…"),
            "install_failed" => T("Update installation failed. Your current version is still installed."),
            "failed" => T("Could not check for updates."),
            _ => string.Format(T("Current version {0}"), current)
        };
        UpdateButton.Content = _updateState switch
        {
            "available" => T("Download and install"),
            "checking" => T("Checking for updates…"),
            "downloading" => string.Format(T("Downloading update… {0}%"), _updateProgress),
            "preparing" => T("Preparing update…"),
            "install_failed" => T("Retry update"),
            _ => T("Check for updates")
        };
        UpdateButton.IsEnabled = !_updateBusy;
        AutomaticUpdatesBox.IsEnabled = !_updateBusy;
        UpdateAvailableButton.Content = "↓  " + T("Update available");
        UpdateAvailableButton.Visibility = _availableUpdate is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateBusy) return;
        _updateBusy = true;
        _updateState = "checking";
        RefreshUpdateText();
        try
        {
            var release = await UpdateService.CheckAsync(_lifetimeCancellation.Token);
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _availableUpdate = release;
            _updateState = release is null ? "current" : "available";
            QueueSave();
            if (release is not null && _tray is not null)
                _tray.ShowBalloonTip(7000, "VoiceMeeter AEC", string.Format(T("Version {0} is available."), release.VersionText), WinForms.ToolTipIcon.Info);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _updateState = manual ? "failed" : "idle";
            DiagnosticsBox.Text += Environment.NewLine + "Update check: " + exception.Message;
            if (manual)
                System.Windows.MessageBox.Show(this, T("Could not check for updates.") + "\n\n" + exception.Message,
                    "VoiceMeeter AEC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _updateBusy = false;
            RefreshUpdateText();
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_updateBusy) return;
        if (_availableUpdate is null)
        {
            await CheckForUpdatesAsync(true);
            return;
        }

        var release = _availableUpdate;
        var answer = System.Windows.MessageBox.Show(this,
            string.Format(T("VoiceMeeter AEC will close, install version {0}, and reopen. Echo cancellation will be interrupted briefly. Continue?"), release.VersionText),
            T("Install update"), MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (answer != MessageBoxResult.OK) return;

        var engineWasRunning = _engine.Alive;
        _updateBusy = true;
        _updateProgress = 0;
        _updateState = "downloading";
        RefreshUpdateText();
        try
        {
            var progress = new Progress<int>(value =>
            {
                _updateProgress = value;
                RefreshUpdateText();
            });
            var stagedPackage = await UpdateService.DownloadAndStageAsync(release, progress, _lifetimeCancellation.Token);
            _updateState = "preparing";
            RefreshUpdateText();
            if (engineWasRunning && !await StopEngine(false))
                throw new InvalidOperationException("The audio engine did not stop in time.");
            SaveSettingsNow();
            UpdateService.StartInstaller(stagedPackage, engineWasRunning);
            _allowClose = true;
            Close();
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _updateState = "install_failed";
            DiagnosticsBox.Text += Environment.NewLine + "Update install: " + exception;
            System.Windows.MessageBox.Show(this, T("The update could not be installed.") + "\n\n" + exception.Message,
                "VoiceMeeter AEC", MessageBoxButton.OK, MessageBoxImage.Error);
            if (engineWasRunning && !_engine.Alive) StartEngine(true);
        }
        finally
        {
            _updateBusy = false;
            RefreshUpdateText();
        }
    }

    private void ShowUpdates()
    {
        ShowWindow();
        ShowPage("advanced");
        Dispatcher.BeginInvoke(() => AdvancedView.ScrollToEnd(), DispatcherPriority.Loaded);
    }

    private void UpdateAvailable_Click(object sender, RoutedEventArgs e) => ShowUpdates();

    private void BeginStartupWait()
    {
        _startupPending = true;
        _startupDeadline = DateTime.Now.AddSeconds(90);
        _startupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _startupTimer.Tick += (_, _) => TryStartup();
        _startupTimer.Start();
        TryStartup();
    }

    private void TryStartup()
    {
        if (!_startupPending)
        {
            _startupTimer?.Stop();
            return;
        }
        if (DateTime.Now >= _startupDeadline)
        {
            _startupPending = false;
            _startupTimer?.Stop();
            UpdateStatusVisual("stopped");
            _tray?.ShowBalloonTip(5000, "VoiceMeeter AEC", T("Please start VoiceMeeter Potato first, then try again."), WinForms.ToolTipIcon.Warning);
            return;
        }
        if (_engine.Alive) return;
        if (Process.GetProcessesByName("voicemeeter8").Length == 0) return;
        StartEngine(true);
    }

    private List<string> BuildEngineArguments()
    {
        SyncSettingsFromControls();
        var start = StripStarts[_settings.MicrophoneStrip - 1];
        var mic = start + (_settings.MicrophoneSide == "right" ? 1 : 0);
        var references = _settings.ReferenceStrips
            .SelectMany(strip => new[] { StripStarts[strip - 1], StripStarts[strip - 1] + 1 })
            .ToList();
        var result = new List<string>
        {
            "--run", "--mic", mic.ToString(), "--ref", string.Join(',', references),
            "--returns", $"{start},{start + 1}", "--hold-ms", _settings.HoldMs.ToString(),
            "--delay-ms", _settings.DelayMs.ToString(), "--auto-strips",
            _settings.WatchAllStrips ? "all" : string.Join(',', _settings.AutoStrips),
            "--auto-bus", _settings.SpeakerBus.ToString(), "--suppression", _settings.Suppression,
            "--" + _settings.StartMode
        };
        return result;
    }

    private void StartEngine(bool quiet = false)
    {
        try
        {
            SaveSettingsNow();
            var enginePath = Path.Combine(AppContext.BaseDirectory, "voicemeeter-aec.exe");
            _engine.Start(enginePath, BuildEngineArguments(), AppSettings.LogPath);
            EngineButton.Content = T("Stop engine");
            SetModeButtons(true);
        }
        catch (Exception exception)
        {
            DiagnosticsBox.Text = exception + Environment.NewLine + _engine.Diagnostics;
            ShowPage("diagnostics");
            if (!quiet)
                System.Windows.MessageBox.Show(this, T("Please start VoiceMeeter Potato first, then try again."), T("Could not start"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task<bool> StopEngine(bool ask)
    {
        if (!_engine.Alive) return true;
        if (ask)
        {
            var answer = System.Windows.MessageBox.Show(this, T("Before stopping, disable the microphone’s PATCH INSERT return in VoiceMeeter. Continue?"), "VoiceMeeter AEC", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) return false;
        }
        var stopped = await _engine.StopAsync(TimeSpan.FromSeconds(2));
        if (!stopped)
            System.Windows.MessageBox.Show(this, T("The engine did not stop yet. Open Diagnostics and try again."), "VoiceMeeter AEC", MessageBoxButton.OK, MessageBoxImage.Warning);
        return stopped;
    }

    private async void Engine_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.Alive)
        {
            if (await StopEngine(true))
            {
                EngineButton.Content = T("Start echo cancellation");
                SetModeButtons(false);
            }
        }
        else StartEngine();
    }

    private void SetModeButtons(bool enabled)
    {
        AutoModeButton.IsEnabled = enabled;
        AecModeButton.IsEnabled = enabled;
        BypassModeButton.IsEnabled = enabled;
        MuteModeButton.IsEnabled = enabled;
    }

    private void SetMode(char command)
    {
        try { _engine.Send(command); }
        catch (Exception exception) { DiagnosticsBox.Text += Environment.NewLine + exception; ShowPage("diagnostics"); }
    }

    private void AutoMode_Click(object sender, RoutedEventArgs e) => SetMode('t');
    private void AecMode_Click(object sender, RoutedEventArgs e) => SetMode('a');
    private void BypassMode_Click(object sender, RoutedEventArgs e) => SetMode('b');
    private void MuteMode_Click(object sender, RoutedEventArgs e) => SetMode('m');
    private void SetupNav_Click(object sender, RoutedEventArgs e) => ShowPage("setup");
    private void GuideNav_Click(object sender, RoutedEventArgs e) => ShowPage("guide");
    private void AdvancedNav_Click(object sender, RoutedEventArgs e) => ShowPage("advanced");
    private void DiagnosticsNav_Click(object sender, RoutedEventArgs e) => ShowPage("diagnostics");

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _settings.Theme = _settings.Theme == "dark" ? "light" : "dark";
        ApplyTheme();
        QueueSave();
    }

    private void ShowPage(string page)
    {
        SetupView.Visibility = page == "setup" ? Visibility.Visible : Visibility.Collapsed;
        GuideView.Visibility = page == "guide" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedView.Visibility = page == "advanced" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsView.Visibility = page == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        SetupNavButton.Tag = page == "setup" ? "active" : null;
        GuideNavButton.Tag = page == "guide" ? "active" : null;
        AdvancedNavButton.Tag = page == "advanced" ? "active" : null;
        DiagnosticsNavButton.Tag = page == "diagnostics" ? "active" : null;
        HeaderTitle.Text = T(page == "setup" ? "Setup" : page == "guide" ? "Setup guide" : page == "advanced" ? "Advanced" : "Diagnostics");
        HeaderSubtitle.Text = page switch
        {
            "guide" => T("Follow the four steps once, then use Setup day to day."),
            "advanced" => T("Fine-tune behaviour after the basic setup works."),
            "diagnostics" => T("Technical activity from the audio engine. Useful when setup is not working."),
            _ => T("Match the columns already visible in VoiceMeeter.")
        };
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async void ExitApplication()
    {
        if (!await StopEngine(true)) return;
        SaveSettingsNow();
        _allowClose = true;
        Close();
        Application.Current.Shutdown();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        if (_engine.Alive)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        SaveSettingsNow();
        _allowClose = true;
        _tray?.Dispose();
        _icon?.Dispose();
        _showTimer.Stop();
        _saveTimer.Stop();
        _engine.Dispose();
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _tray?.Dispose();
        _icon?.Dispose();
        _showTimer.Stop();
        _saveTimer.Stop();
        _engine.Dispose();
        base.OnClosed(e);
    }
}
