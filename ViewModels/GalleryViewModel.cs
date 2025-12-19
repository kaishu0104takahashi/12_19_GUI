using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using GUI_12_19.Models;
using GUI_12_19.Services;
using System.Globalization;
using System.IO; 
using System.Text.RegularExpressions;

namespace GUI_12_19.ViewModels;

public enum GalleryViewMode {
    List,
    Detail
}

public class GalleryDateGroupViewModel : ViewModelBase
{
    public string DateHeader { get; }
    public ObservableCollection<GalleryItemViewModel> Items { get; } = new();

    public GalleryDateGroupViewModel(IGrouping<string, InspectionRecord> group, GalleryViewModel parentVM)
    {
        if (DateTime.TryParseExact(group.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            DateHeader = date.ToString("yyyy年MM月dd日");
        }
        else
        {
            DateHeader = group.Key;
        }

        foreach (var record in group)
        {
            Items.Add(new GalleryItemViewModel(record, parentVM));
        }
    }
}

public class GalleryViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly DatabaseService _dbService;
    private List<InspectionRecord> _allRecords = new();

    public ObservableCollection<GalleryDateGroupViewModel> DisplayGroups { get; } = new();

    private GalleryViewMode _currentMode = GalleryViewMode.List;
    public GalleryViewMode CurrentMode
    {
        get => _currentMode;
        set { _currentMode = value; RaisePropertyChanged(); }
    }

    // --- 削除モード ---
    private bool _isDeleteMode = false;
    public bool IsDeleteMode
    {
        get => _isDeleteMode;
        set 
        { 
            _isDeleteMode = value; 
            RaisePropertyChanged(); 
            // 名前変更モードは解除
            if(value) IsRenameMode = false;
            UpdateItemsMode();
            RaisePropertyChanged(nameof(DeleteButtonText));
            RaisePropertyChanged(nameof(StatusMessage));
        }
    }
    
    // --- 名前変更モード ---
    private bool _isRenameMode = false;
    public bool IsRenameMode
    {
        get => _isRenameMode;
        set
        {
            _isRenameMode = value;
            RaisePropertyChanged();
            // 削除モードは解除
            if(value) IsDeleteMode = false;
            UpdateItemsMode();
            RaisePropertyChanged(nameof(RenameButtonText));
            RaisePropertyChanged(nameof(StatusMessage));
        }
    }

    // --- 名前変更ダイアログ ---
    private bool _showRenameDialog = false;
    public bool ShowRenameDialog
    {
        get => _showRenameDialog;
        set { _showRenameDialog = value; RaisePropertyChanged(); }
    }
    
    private string _renameInput = "";
    public string RenameInput
    {
        get => _renameInput;
        set 
        {
            // 半角英数字のみ許可
            if (Regex.IsMatch(value, "^[a-zA-Z0-9_-]*$"))
            {
                _renameInput = value;
                RaisePropertyChanged();
            }
        }
    }
    
    private GalleryItemViewModel? _targetItemForRename;

    // --- 削除確認ダイアログ ---
    private bool _showDeleteConfirm = false;
    public bool ShowDeleteConfirm
    {
        get => _showDeleteConfirm;
        set { _showDeleteConfirm = value; RaisePropertyChanged(); }
    }

    public string DeleteButtonText => IsDeleteMode ? "実行" : "削除";
    public string RenameButtonText => IsRenameMode ? "キャンセル" : "名前変更";
    public string StatusMessage
    {
        get
        {
            if (IsDeleteMode) return "削除する画像を選択してください";
            if (IsRenameMode) return "名前を変更する画像を選択してください";
            return "条件指定";
        }
    }

    private GalleryDetailViewModel? _detailViewModel;
    public GalleryDetailViewModel? DetailViewModel
    {
        get => _detailViewModel;
        set { _detailViewModel = value; RaisePropertyChanged(); }
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; RaisePropertyChanged(); ApplyFilter(); }
    }

    private bool _showSimple = true;
    public bool ShowSimple
    {
        get => _showSimple;
        set { _showSimple = value; RaisePropertyChanged(); ApplyFilter(); }
    }

    private bool _showPrecision = true;
    public bool ShowPrecision
    {
        get => _showPrecision;
        set { _showPrecision = value; RaisePropertyChanged(); ApplyFilter(); }
    }

    public ICommand HeaderDeleteButtonCommand { get; }
    public ICommand HeaderRenameButtonCommand { get; }
    
    public ICommand ExecuteDeleteConfirmCommand { get; }
    public ICommand CancelDeleteConfirmCommand { get; }

    public ICommand ExecuteRenameCommand { get; }
    public ICommand CancelRenameCommand { get; }
    
    public ICommand ShowDetailCommand { get; }
    public ICommand BackToListCommand { get; }
    public ICommand BackCommand { get; }

    public GalleryViewModel(MainViewModel main)
    {
        _main = main;
        _dbService = new DatabaseService();
        
        BackCommand = new RelayCommand(() => 
        {
            if (IsDeleteMode || IsRenameMode)
            {
                QuitModes();
            }
            else
            {
                _main.Navigate(new HomeViewModel(_main));
            }
        });

        BackToListCommand = new RelayCommand(ExecuteBackToList);
        ShowDetailCommand = new RelayCommand<InspectionRecord>(ExecuteShowDetail);

        // 削除ボタン
        HeaderDeleteButtonCommand = new RelayCommand(() =>
        {
            if (!IsDeleteMode)
            {
                IsDeleteMode = true;
            }
            else
            {
                if (CountSelectedItems() > 0) ShowDeleteConfirm = true;
                else QuitModes();
            }
        });
        
        // 名前変更ボタン
        HeaderRenameButtonCommand = new RelayCommand(() =>
        {
            IsRenameMode = !IsRenameMode;
        });

        // 削除実行
        ExecuteDeleteConfirmCommand = new RelayCommand(PerformDeletion);
        CancelDeleteConfirmCommand = new RelayCommand(() => { ShowDeleteConfirm = false; QuitModes(); });

        // 名前変更実行
        ExecuteRenameCommand = new RelayCommand(PerformRename);
        CancelRenameCommand = new RelayCommand(() => { ShowRenameDialog = false; _targetItemForRename = null; });

        LoadData();
    }
    
    // アイテムクリック時に呼ばれる（Renameモードならダイアログを開く）
    public void OnItemClicked(GalleryItemViewModel item)
    {
        if (IsRenameMode)
        {
            _targetItemForRename = item;
            RenameInput = item.Record.SaveName;
            ShowRenameDialog = true;
        }
    }

    private void UpdateItemsMode()
    {
        foreach (var group in DisplayGroups)
        {
            foreach (var item in group.Items)
            {
                item.IsDeleteMode = IsDeleteMode;
                item.IsRenameMode = IsRenameMode;
                if (!IsDeleteMode) item.IsSelected = false;
            }
        }
    }

    private int CountSelectedItems()
    {
        int count = 0;
        foreach (var group in DisplayGroups)
            foreach (var item in group.Items)
                if (item.IsSelected) count++;
        return count;
    }

    private void QuitModes()
    {
        ShowDeleteConfirm = false;
        ShowRenameDialog = false;
        IsDeleteMode = false;
        IsRenameMode = false;
    }

    private void PerformDeletion()
    {
        var itemsToDelete = new List<GalleryItemViewModel>();
        foreach (var group in DisplayGroups)
        {
            foreach (var item in group.Items)
            {
                if (item.IsSelected) itemsToDelete.Add(item);
            }
        }

        foreach (var item in itemsToDelete)
        {
            _dbService.DeleteInspection(item.Record.Id);
            if (Directory.Exists(item.Record.SaveAbsolutePath))
            {
                try { Directory.Delete(item.Record.SaveAbsolutePath, true); }
                catch (Exception ex) { Console.WriteLine($"File Delete Error: {ex.Message}"); }
            }
        }

        QuitModes();
        LoadData();
    }

    private void PerformRename()
    {
        if (_targetItemForRename == null || string.IsNullOrWhiteSpace(RenameInput)) return;

        string oldName = _targetItemForRename.Record.SaveName;
        string newName = RenameInput;
        string oldPath = _targetItemForRename.Record.SaveAbsolutePath;
        
        // パスの構築 (/home/shikoku-pc/pic/NewName)
        string parentDir = Path.GetDirectoryName(oldPath) ?? "/home/shikoku-pc/pic";
        string newPath = Path.Combine(parentDir, newName);

        if (oldName == newName)
        {
            ShowRenameDialog = false;
            return;
        }

        try
        {
            if (Directory.Exists(newPath))
            {
                // 重複エラーなどのハンドリングが必要だが、簡易的に今回は何もしないかエラーログ
                Console.WriteLine("既に同名のフォルダが存在します");
                return;
            }

            // 1. ディレクトリ名の変更
            Directory.Move(oldPath, newPath);

            // 2. フォルダ内のファイル名の変更 (○○_circuit_omote.jpg など)
            string[] files = Directory.GetFiles(newPath);
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Contains(oldName))
                {
                    string newFileName = fileName.Replace(oldName, newName);
                    string newFilePath = Path.Combine(newPath, newFileName);
                    File.Move(file, newFilePath);
                }
            }

            // 3. データベース更新
            _dbService.UpdateInspectionName(_targetItemForRename.Record.Id, newName, newPath);
            
            // 4. リフレッシュ
            ShowRenameDialog = false;
            IsRenameMode = false; // 変更したら通常モードへ戻る
            LoadData();

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Rename Error: {ex.Message}");
        }
    }

    private void ExecuteShowDetail(InspectionRecord? record)
    {
        if (IsDeleteMode || IsRenameMode) return;

        if (record != null)
        {
            DetailViewModel = new GalleryDetailViewModel(record, this);
            CurrentMode = GalleryViewMode.Detail; 
        }
    }
    
    private void ExecuteBackToList()
    {
        DetailViewModel = null;
        CurrentMode = GalleryViewMode.List;
    }

    private void LoadData()
    {
        _dbService.Initialize();
        _allRecords = _dbService.GetAllRecords();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        DisplayGroups.Clear();
        ExecuteBackToList();
        IsDeleteMode = false; 
        IsRenameMode = false;

        var filtered = _allRecords.Where(r =>
        {
            bool matchText = string.IsNullOrEmpty(SearchText) ||
                             r.SaveName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                             r.Date.Contains(SearchText);
             
            bool matchType = (r.Type == 0 && ShowSimple) || (r.Type == 1 && ShowPrecision);
            return matchText && matchType;
        })
        .GroupBy(r => r.Date.Length >= 10 ? r.Date.Substring(0, 10) : r.Date) 
        .OrderByDescending(g => g.Key);

        foreach (var group in filtered)
        {
            DisplayGroups.Add(new GalleryDateGroupViewModel(group, this));
        }
    }
}

public class GalleryItemViewModel : ViewModelBase
{
    public InspectionRecord Record { get; }
    public Bitmap? Thumbnail { get; }
    public string TypeLabel => Record.Type == 0 ? "簡易" : "精密";
    public string LabelColor => Record.Type == 0 ? "#007ACC" : "#E06C00";
    
    private bool _isDeleteMode = false;
    public bool IsDeleteMode
    {
        get => _isDeleteMode;
        set { _isDeleteMode = value; RaisePropertyChanged(); }
    }
    
    private bool _isRenameMode = false;
    public bool IsRenameMode
    {
        get => _isRenameMode;
        set { _isRenameMode = value; RaisePropertyChanged(); }
    }

    private bool _isSelected = false;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; RaisePropertyChanged(); }
    }
    
    public ICommand ItemClickCommand { get; } 

    public GalleryItemViewModel(InspectionRecord record, GalleryViewModel parentVM)
    {
        Record = record;
        
        ItemClickCommand = new RelayCommand(() => 
        {
            if (parentVM.IsDeleteMode)
            {
                IsSelected = !IsSelected;
            }
            else if (parentVM.IsRenameMode)
            {
                // 名前変更モードなら親VMのメソッドを呼んでダイアログを開く
                parentVM.OnItemClicked(this);
            }
            else
            {
                parentVM.ShowDetailCommand.Execute(record);
            }
        });
        
        try
        {
            if (File.Exists(record.ThumbnailPath))
            {
                using (var stream = File.OpenRead(record.ThumbnailPath))
                {
                    Thumbnail = Bitmap.DecodeToWidth(stream, 320);
                }
            }
        }
        catch (Exception) { Thumbnail = null; }
    }
}

public class GalleryDetailViewModel : ViewModelBase
{
    public InspectionRecord Record { get; }
    
    public Bitmap? OmoteImage { get; }
    public Bitmap? UraImage { get; }
    public Bitmap? CircuitOmoteImage { get; }
    public Bitmap? CircuitUraImage { get; }
    
    public bool IsPrecision => Record.Type == 1;
    public string TypeLabel => Record.Type == 0 ? "簡易検査" : "精密検査";
    public string FormattedDate => DateTime.TryParseExact(Record.Date, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date.ToString("yyyy年MM月dd日 HH時mm分ss秒") : Record.Date;
    public string BoardInfo => "基板情報: 取得中 (後で実装)";
    
    public ICommand CloseDetailCommand { get; }

    public GalleryDetailViewModel(InspectionRecord record, GalleryViewModel parent)
    {
        Record = record;
        CloseDetailCommand = parent.BackToListCommand;
        
        string omotePath = "";
        string uraPath = "";
        string circuitOmotePath = "";
        string circuitUraPath = "";

        if (record.Type == 1) 
        {
            omotePath = record.PrecisionPcbOmotePath;
            uraPath = record.PrecisionPcbUraPath;
            circuitOmotePath = record.PrecisionCircuitOmotePath;
            circuitUraPath = record.PrecisionCircuitUraPath;
        }
        else 
        {
            omotePath = record.SimpleOmotePath;
            uraPath = record.SimpleUraPath;
        }

        Bitmap? LoadImage(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (File.Exists(path))
            {
                try { return new Bitmap(path); }
                catch { return null; }
            }
            return null;
        }

        OmoteImage = LoadImage(omotePath);
        UraImage = LoadImage(uraPath);
        if (IsPrecision)
        {
            CircuitOmoteImage = LoadImage(circuitOmotePath);
            CircuitUraImage = LoadImage(circuitUraPath);
        }
    }
}
