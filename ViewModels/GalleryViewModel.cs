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
    
    // フィルタリング済みのフラットなリスト
    private List<InspectionRecord> _filteredRecordsList = new();

    public ObservableCollection<GalleryDateGroupViewModel> DisplayGroups { get; } = new();

    private GalleryViewMode _currentMode = GalleryViewMode.List;
    public GalleryViewMode CurrentMode
    {
        get => _currentMode;
        set { _currentMode = value; RaisePropertyChanged(); }
    }

    // --- 削除・名前変更モード管理 ---
    private bool _isDeleteMode = false;
    public bool IsDeleteMode
    {
        get => _isDeleteMode;
        set 
        { 
            _isDeleteMode = value; 
            RaisePropertyChanged(); 
            if(value) IsRenameMode = false;
            UpdateItemsMode();
            RaisePropertyChanged(nameof(DeleteButtonText));
            RaisePropertyChanged(nameof(StatusMessage));
        }
    }
    
    private bool _isRenameMode = false;
    public bool IsRenameMode
    {
        get => _isRenameMode;
        set
        {
            _isRenameMode = value;
            RaisePropertyChanged();
            if(value) IsDeleteMode = false;
            UpdateItemsMode();
            RaisePropertyChanged(nameof(RenameButtonText));
            RaisePropertyChanged(nameof(StatusMessage));
        }
    }

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
            if (Regex.IsMatch(value, "^[a-zA-Z0-9_-]*$"))
            {
                _renameInput = value;
                RaisePropertyChanged();
            }
        }
    }
    
    private GalleryItemViewModel? _targetItemForRename;

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
            if (IsDeleteMode || IsRenameMode) QuitModes();
            else _main.Navigate(new HomeViewModel(_main));
        });

        BackToListCommand = new RelayCommand(ExecuteBackToList);
        
        // リストからのクリックは常に「先頭から」表示
        ShowDetailCommand = new RelayCommand<InspectionRecord>(r => OpenDetail(r, startFromEnd: false));

        HeaderDeleteButtonCommand = new RelayCommand(() =>
        {
            if (!IsDeleteMode) IsDeleteMode = true;
            else
            {
                if (CountSelectedItems() > 0) ShowDeleteConfirm = true;
                else QuitModes();
            }
        });
        
        HeaderRenameButtonCommand = new RelayCommand(() => { IsRenameMode = !IsRenameMode; });

        ExecuteDeleteConfirmCommand = new RelayCommand(PerformDeletion);
        CancelDeleteConfirmCommand = new RelayCommand(() => { ShowDeleteConfirm = false; QuitModes(); });

        ExecuteRenameCommand = new RelayCommand(PerformRename);
        CancelRenameCommand = new RelayCommand(() => { ShowRenameDialog = false; _targetItemForRename = null; });

        LoadData();
    }
    
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
        string parentDir = Path.GetDirectoryName(oldPath) ?? "/home/shikoku-pc/pic";
        string newPath = Path.Combine(parentDir, newName);

        if (oldName == newName) { ShowRenameDialog = false; return; }

        try
        {
            if (Directory.Exists(newPath)) { Console.WriteLine("既に同名のフォルダが存在します"); return; }

            Directory.Move(oldPath, newPath);
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

            _dbService.UpdateInspectionName(_targetItemForRename.Record.Id, newName, newPath);
            ShowRenameDialog = false;
            IsRenameMode = false;
            LoadData();
        }
        catch (Exception ex) { Console.WriteLine($"Rename Error: {ex.Message}"); }
    }

    // --- 【修正】詳細画面を開く処理 ---
    // startFromEnd: trueなら最後の画像から表示、falseなら最初の画像から表示
    public void OpenDetail(InspectionRecord? record, bool startFromEnd)
    {
        if (IsDeleteMode || IsRenameMode || record == null) return;
        DetailViewModel = new GalleryDetailViewModel(record, this, startFromEnd);
        CurrentMode = GalleryViewMode.Detail; 
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

        var filteredQuery = _allRecords.Where(r =>
        {
            bool matchText = string.IsNullOrEmpty(SearchText) ||
                             r.SaveName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                             r.Date.Contains(SearchText);
            bool matchType = (r.Type == 0 && ShowSimple) || (r.Type == 1 && ShowPrecision);
            return matchText && matchType;
        })
        .OrderByDescending(g => g.Date); 

        _filteredRecordsList = filteredQuery.ToList();

        var grouped = filteredQuery
            .GroupBy(r => r.Date.Length >= 10 ? r.Date.Substring(0, 10) : r.Date);

        foreach (var group in grouped)
        {
            DisplayGroups.Add(new GalleryDateGroupViewModel(group, this));
        }
    }

    // --- レコード間移動ロジック ---
    public bool CanGoNextRecord(InspectionRecord current)
    {
        int index = _filteredRecordsList.FindIndex(r => r.Id == current.Id);
        return index >= 0 && index < _filteredRecordsList.Count - 1;
    }

    public bool CanGoPreviousRecord(InspectionRecord current)
    {
        int index = _filteredRecordsList.FindIndex(r => r.Id == current.Id);
        return index > 0;
    }

    public void GoToNextRecord(InspectionRecord current)
    {
        int index = _filteredRecordsList.FindIndex(r => r.Id == current.Id);
        if (index >= 0 && index < _filteredRecordsList.Count - 1)
        {
            // 次へ (右) 押下時 -> 次のレコードの「先頭」へ
            OpenDetail(_filteredRecordsList[index + 1], startFromEnd: false);
        }
    }

    public void GoToPreviousRecord(InspectionRecord current)
    {
        int index = _filteredRecordsList.FindIndex(r => r.Id == current.Id);
        if (index > 0)
        {
            // 前へ (左) 押下時 -> 前のレコードの「末尾」へ
            OpenDetail(_filteredRecordsList[index - 1], startFromEnd: true);
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
            if (parentVM.IsDeleteMode) IsSelected = !IsSelected;
            else if (parentVM.IsRenameMode) parentVM.OnItemClicked(this);
            else parentVM.ShowDetailCommand.Execute(record);
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
    private readonly GalleryViewModel _parentVM;
    public InspectionRecord Record { get; }
    
    private List<Bitmap> _images = new();
    
    private Bitmap? _currentImage;
    public Bitmap? CurrentImage
    {
        get => _currentImage;
        set { _currentImage = value; RaisePropertyChanged(); }
    }

    private int _currentPageIndex = 0;
    
    public string PageIndicator => _images.Count > 0 ? $"{_currentPageIndex + 1} / {_images.Count}" : "";
    
    public bool CanGoPreviousImage => _currentPageIndex > 0;
    public bool CanGoNextImage => _currentPageIndex < _images.Count - 1;

    public bool CanMovePrevious => CanGoPreviousImage || _parentVM.CanGoPreviousRecord(Record);
    public bool CanMoveNext => CanGoNextImage || _parentVM.CanGoNextRecord(Record);

    public string TypeLabel => Record.Type == 0 ? "簡易検査" : "精密検査";
    public string FormattedDate => DateTime.TryParseExact(Record.Date, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date.ToString("yyyy年MM月dd日 HH時mm分ss秒") : Record.Date;
    public string BoardInfo => "基板情報: 取得中";
    
    public ICommand CloseDetailCommand { get; }
    public ICommand MoveNextCommand { get; }
    public ICommand MovePreviousCommand { get; }

    // --- 【修正】コンストラクタで開始位置(startFromEnd)を受け取る ---
    public GalleryDetailViewModel(InspectionRecord record, GalleryViewModel parent, bool startFromEnd)
    {
        Record = record;
        _parentVM = parent;
        CloseDetailCommand = parent.BackToListCommand;
        
        var paths = new List<string>();
        if (record.Type == 1) 
        {
            paths.Add(record.PrecisionPcbOmotePath);
            paths.Add(record.PrecisionPcbUraPath);
            paths.Add(record.PrecisionCircuitOmotePath);
            paths.Add(record.PrecisionCircuitUraPath);
        }
        else 
        {
            paths.Add(record.SimpleOmotePath);
            paths.Add(record.SimpleUraPath);
        }

        foreach (var path in paths)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try { _images.Add(new Bitmap(path)); }
                catch { }
            }
        }

        // --- 初期表示画像の設定 ---
        if (_images.Count > 0)
        {
            if (startFromEnd)
            {
                // 後ろからアクセスされた場合は最後の画像を表示
                _currentPageIndex = _images.Count - 1;
            }
            else
            {
                // 通常または前からアクセスされた場合は最初の画像
                _currentPageIndex = 0;
            }
            CurrentImage = _images[_currentPageIndex];
        }
        
        UpdateNavigationState();

        MoveNextCommand = new RelayCommand(() =>
        {
            if (CanGoNextImage)
            {
                _currentPageIndex++;
                CurrentImage = _images[_currentPageIndex];
                UpdateNavigationState();
            }
            else if (_parentVM.CanGoNextRecord(Record))
            {
                _parentVM.GoToNextRecord(Record);
            }
        });

        MovePreviousCommand = new RelayCommand(() =>
        {
            if (CanGoPreviousImage)
            {
                _currentPageIndex--;
                CurrentImage = _images[_currentPageIndex];
                UpdateNavigationState();
            }
            else if (_parentVM.CanGoPreviousRecord(Record))
            {
                _parentVM.GoToPreviousRecord(Record);
            }
        });
    }

    private void UpdateNavigationState()
    {
        RaisePropertyChanged(nameof(PageIndicator));
        RaisePropertyChanged(nameof(CanMovePrevious));
        RaisePropertyChanged(nameof(CanMoveNext));
    }
}
