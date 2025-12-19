using System.Windows.Input;

namespace GUI_12_19.ViewModels;

public class HomeViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    public ICommand CaptureCommand { get; } 
    public ICommand GalleryCommand { get; }
    public ICommand DetailCommand { get; } 
    public ICommand StopCommand { get; }
    
    public HomeViewModel(MainViewModel main)
    {
        _main = main;
        CaptureCommand = new RelayCommand(() => _main.Navigate(new SimpleInspectViewModel(_main)));
        GalleryCommand = new RelayCommand(() => _main.Navigate(new GalleryViewModel(_main)));
        DetailCommand = new RelayCommand(() => { /* TODO */ });
        StopCommand = new RelayCommand(_main.ShutdownApplication);
    }
}
