using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using GUI_12_19.Services;

namespace GUI_12_19.ViewModels;

public class SimpleInspectViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly DatabaseService _dbService;

    // --- UIの状態管理用プロパティ ---
    
    private string _statusMessage = "撮影ボタンを押してください";
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; RaisePropertyChanged(); }
    }

    // 画面切り替えフラグ
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

    private bool _isNamingPrompt = false; // 名前付け確認中
    public bool IsNamingPrompt
    {
        get => _isNamingPrompt;
        set { _isNamingPrompt = value; RaisePropertyChanged(); }
    }
    
    private bool _isNamingInput = false; // 名前入力中
    public bool IsNamingInput
    {
        get => _isNamingInput;
        set { _isNamingInput = value; RaisePropertyChanged(); }
    }

    // 撮影した画像のキャッシュ (保存用)
    public Bitmap? CapturedImage { get; set; }

    // 名前入力フィールド
    private string _inputName = "";
    public string InputName
    {
        get => _inputName;
        set 
        {
            if (Regex.IsMatch(value, "^[a-zA-Z0-9]*$"))
            {
                _inputName = value;
                RaisePropertyChanged();
            }
        }
    }

    // --- コマンド ---
    public ICommand CaptureCommand { get; }
    public ICommand RetakeCommand { get; }
    public ICommand ConfirmCaptureCommand { get; }
    public ICommand YesNameCommand { get; }
    public ICommand NoNameCommand { get; }
    public ICommand SaveWithNameCommand { get; }
    public ICommand BackCommand { get; }

    public SimpleInspectViewModel(MainViewModel main)
    {
        _main = main;
        _dbService = new DatabaseService();

        BackCommand = new RelayCommand(() => 
        {
            // 戻る際は必ずカメラを再開
            _main.IsCameraPaused = false;
            _main.Navigate(new HomeViewModel(_main));
        });

        // 撮影
        CaptureCommand = new RelayCommand(() =>
        {
            if (_main.CameraImage != null)
            {
                // 1. 現在の画像を保存用に確保
                CapturedImage = _main.CameraImage;
                
                // 2. MainViewModelの更新を停止（これで左画面がフリーズする）
                _main.IsCameraPaused = true;
                
                // 3. UI遷移
                IsMonitoring = false;
                IsReviewing = true;
                StatusMessage = "この画像でよろしいですか？";
            }
        });

        // 再撮影 (いいえ)
        RetakeCommand = new RelayCommand(() =>
        {
            CapturedImage = null;
            // カメラ再開
            _main.IsCameraPaused = false;
            
            // UI遷移
            IsReviewing = false;
            IsMonitoring = true;
            StatusMessage = "撮影ボタンを押してください";
        });

        // 画像OK (はい)
        ConfirmCaptureCommand = new RelayCommand(() =>
        {
            IsReviewing = false;
            IsNamingPrompt = true;
            StatusMessage = "名前を付けて保存しますか？";
        });

        YesNameCommand = new RelayCommand(() =>
        {
            IsNamingPrompt = false;
            IsNamingInput = true;
            InputName = "";
            StatusMessage = "保存名を入力してください (半角英数のみ)";
        });

        NoNameCommand = new RelayCommand(() =>
        {
            string dateStr = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            SaveProcess(dateStr);
        });

        SaveWithNameCommand = new RelayCommand(() =>
        {
            if (string.IsNullOrEmpty(InputName)) return;
            SaveProcess(InputName);
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

            string fileName = $"{saveName}_omote.jpg";
            string fullPath = Path.Combine(saveDir, fileName);

            CapturedImage?.Save(fullPath);

            string dateStr = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            _dbService.InsertInspection(saveName, saveDir, dateStr, 0);

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
