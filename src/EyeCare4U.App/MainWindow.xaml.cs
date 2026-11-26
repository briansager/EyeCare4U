using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using EyeCare4U.App.ViewModels;

namespace EyeCare4U.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string,object> _normalBrushes=[];
    private HistoryViewModel? _history;
    private bool _confirmationVisible;
    private bool _returnHistoryFocus;
    public MainWindow()
    {
        InitializeComponent();
        foreach(var key in new[]{"PageBrush","BodyBrush","AccentBrush","MutedBrush","PanelBrush","LineBrush","ActivityBrush","ErrorBrush","ConfirmBrush"})
            _normalBrushes[key]=Resources[key];
        ApplyContrast(); SystemParameters.StaticPropertyChanged+=OnSystemParametersChanged;
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainWindowViewModel oldViewModel) oldViewModel.PropertyChanged -= OnViewModelChanged;
            if (e.NewValue is MainWindowViewModel newViewModel) newViewModel.PropertyChanged += OnViewModelChanged;
            AttachHistory((e.NewValue as MainWindowViewModel)?.History);
        };
        Closed += (_, _) =>
        {
            if(DataContext is MainWindowViewModel vm) vm.PropertyChanged-=OnViewModelChanged;
            AttachHistory(null); SystemParameters.StaticPropertyChanged-=OnSystemParametersChanged;
        };
    }

    private void OnSystemParametersChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(SystemParameters.HighContrast)) _ = Dispatcher.InvokeAsync(ApplyContrast);
    }
    private void ApplyContrast()
    {
        foreach(var (key,normal) in _normalBrushes)
            Resources[key]=!SystemParameters.HighContrast?normal:key switch
            {
                "BodyBrush" or "MutedBrush" or "AccentBrush" or "ErrorBrush"=>SystemColors.WindowTextBrush,
                "LineBrush"=>SystemColors.WindowTextBrush,
                _=>SystemColors.WindowBrush
            };
    }
    private void AttachHistory(HistoryViewModel? history)
    {
        if(ReferenceEquals(_history,history)) return;
        if(_history is not null) _history.PropertyChanged-=OnHistoryChanged;
        _history=history;
        if(_history is not null) _history.PropertyChanged+=OnHistoryChanged;
    }
    private void OnHistoryChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(_history is null) return;
        if(_history.ClearConfirmationVisible!=_confirmationVisible)
        {
            _confirmationVisible=_history.ClearConfirmationVisible;
            if(_confirmationVisible) _ = Dispatcher.InvokeAsync(()=>
            {
                if(IsVisible && _history?.ClearConfirmationVisible==true) { KeepHistoryButton.BringIntoView(); KeepHistoryButton.Focus(); }
            },System.Windows.Threading.DispatcherPriority.Loaded);
            else _returnHistoryFocus=true;
        }
        if(_returnHistoryFocus && !_history.IsBusy)
        {
            _returnHistoryFocus=false;
            _ = Dispatcher.InvokeAsync(()=> { if(IsVisible) HistoryClearButton.Focus(); },System.Windows.Threading.DispatcherPriority.Loaded);
        }
        if(e.PropertyName==nameof(HistoryViewModel.Announcement)) Announce(HistoryAnnouncementText);
    }
    private void OnConfirmationKeyDown(object sender,System.Windows.Input.KeyEventArgs e)
    {
        if(e.Key==Key.Escape && _history?.CancelClearCommand.CanExecute(null)==true)
        { _history.CancelClearCommand.Execute(null); e.Handled=true; }
    }
    private void Announce(UIElement element)
    {
        if(!IsVisible || !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        _ = Dispatcher.InvokeAsync(()=>
        {
            if(IsVisible) UIElementAutomationPeer.CreatePeerForElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        },System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void HideToTray(object sender, RoutedEventArgs e) => Hide();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(MainWindowViewModel.History)) AttachHistory((sender as MainWindowViewModel)?.History);
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
