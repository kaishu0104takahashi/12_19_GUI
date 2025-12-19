using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using GUI_12_19.Services;

namespace GUI_12_19.ViewModels;

public class SimpleInspectViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly DatabaseService _dbService;

    // --- UIの状態管理 ---
    
    private string _statusMessage = "撮影ボタンを押してください";
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; RaisePropertyChanged(); }
    }

    private bool _isMonitoring = true; // 撮影待機中
    public bool IsMonitoring
    {
        get => _isMonitoring;
        set { _isMonitoring = value; RaisePropertyChanged(); }
    }

    private bool _isReviewing = false; // 画像確認中
    public bool IsReviewing
    {
        get => _isReviewing;
        set { _isReviewing = value; RaisePropertyChanged(); }
    }

    // 撮影した画像のキャッシュ
    public Bitmap? CapturedImage { get; set; }

    // --- コマンド ---
    public ICommand CaptureCommand { get; }
    public ICommand RetakeCommand { get; }
    public ICommand ConfirmCaptureCommand { get; }
    public ICommand BackCommand { get; }

    public SimpleInspectViewModel(MainViewModel main)
    {
        _main = main;
        _dbService = new DatabaseService();

        BackCommand = new RelayCommand(() => 
        {
            _main.IsCameraPaused = false;
            _main.Navigate(new HomeViewModel(_main));
        });

        // 撮影
        CaptureCommand = new RelayCommand(() =>
        {
            if (_main.CameraImage != null)
            {
                CapturedImage = _main.CameraImage;
                _main.IsCameraPaused = true;
                
                IsMonitoring = false;
                IsReviewing = true;
                StatusMessage = "この画像でよろしいですか？";
            }
        });

        // 再撮影 (いいえ)
        RetakeCommand = new RelayCommand(() =>
        {
            CapturedImage = null;
            _main.IsCameraPaused = false;
            
            IsReviewing = false;
            IsMonitoring = true;
            StatusMessage = "撮影ボタンを押してください";
        });

        // 画像OK (はい) -> 即座に日時で保存
        ConfirmCaptureCommand = new RelayCommand(() =>
        {
            // YYYY-MM-DD-hh-mm-ss 形式
            string dateStr = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            SaveProcess(dateStr);
        });
    }

    private void SaveProcess(string saveName)
    {
        try
        {
            string basePath = "/home/shikoku-pc/pic";
            string saveDir = Path.Combine(basePath, saveName);

            if (!Directory.Exists(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            // ファイル名もディレクトリ名に合わせる
            string fileName = $"{saveName}_omote.jpg";
            string fullPath = Path.Combine(saveDir, fileName);

            CapturedImage?.Save(fullPath);

            // DB保存
            // save_absolute_path はディレクトリパスを保存するのが通例になっているためそれに従う
            _dbService.InsertInspection(saveName, saveDir, saveName, 0); // dateもsaveNameと同じ形式でよい

            // 保存完了後、カメラを再開してホームへ
            _main.IsCameraPaused = false;
            _main.Navigate(new HomeViewModel(_main));
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}";
        }
    }
}
