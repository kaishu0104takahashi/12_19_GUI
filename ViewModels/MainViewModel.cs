using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using GUI_12_19.Services;

namespace GUI_12_19.ViewModels;

public class MainViewModel : ViewModelBase
{
    private ViewModelBase _currentViewModel;
    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        set { _currentViewModel = value; RaisePropertyChanged(); UpdateViewStatus(value); }
    }
    
    private bool _isGalleryActive = false;
    public bool IsGalleryActive
    {
        get => _isGalleryActive;
        set { _isGalleryActive = value; RaisePropertyChanged(); }
    }

    // --- 動画表示用プロパティ ---
    private Bitmap? _cameraImage;
    public Bitmap? CameraImage
    {
        get => _cameraImage;
        set { _cameraImage = value; RaisePropertyChanged(); }
    }

    // --- 映像更新の一時停止フラグ (新規追加) ---
    private bool _isCameraPaused = false;
    public bool IsCameraPaused
    {
        get => _isCameraPaused;
        set { _isCameraPaused = value; RaisePropertyChanged(); }
    }

    // --- UDP受信サービス ---
    private readonly UdpVideoReceiver _udpReceiver;

    public MainViewModel()
    {
        // 初期画面
        _currentViewModel = new HomeViewModel(this);

        // UDP受信サービスの初期化
        _udpReceiver = new UdpVideoReceiver(50000);
        
        // コールバック設定: ポーズ中でなければ画像を更新
        _udpReceiver.OnFrameReady = (bitmap) => 
        {
            if (!IsCameraPaused)
            {
                CameraImage = bitmap;
            }
        };

        // 受信開始
        _udpReceiver.Start();
    }
    
    public void Navigate(ViewModelBase viewModel)
    {
        CurrentViewModel = viewModel;
    }
    
    private void UpdateViewStatus(ViewModelBase viewModel)
    {
        IsGalleryActive = viewModel is GalleryViewModel;
        // 画面遷移時は念のためポーズを解除（必要に応じて調整）
        IsCameraPaused = false;
    }

    public void ShutdownApplication()
    {
        _udpReceiver.Stop();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
