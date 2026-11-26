using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using EyeCare4U.App.ViewModels;

namespace EyeCare4U.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainWindowViewModel oldViewModel) oldViewModel.PropertyChanged -= OnViewModelChanged;
            if (e.NewValue is MainWindowViewModel newViewModel) newViewModel.PropertyChanged += OnViewModelChanged;
        };
        Closed += (_, _) => ((MainWindowViewModel)DataContext).PropertyChanged -= OnViewModelChanged;
    }

    private void HideToTray(object sender, RoutedEventArgs e) => Hide();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsVisible || !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        var element = e.PropertyName switch
        {
            nameof(MainWindowViewModel.Status) => StatusText,
            nameof(MainWindowViewModel.ValidationError) => ValidationText,
            nameof(MainWindowViewModel.ThresholdValidationError) => ThresholdValidationText,
            nameof(MainWindowViewModel.ActivityStatusText) => ActivityStatusText,
            nameof(MainWindowViewModel.ExperienceResult) => ExperienceResultText,
            nameof(MainWindowViewModel.DeliveryNote) => DeliveryNoteText,
            _ => null
        };
        if (element is null) return;
        // Let data bindings update the text before assistive technology queries it.
        Dispatcher.InvokeAsync(() =>
        {
            if (IsVisible)
                UIElementAutomationPeer.CreatePeerForElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
