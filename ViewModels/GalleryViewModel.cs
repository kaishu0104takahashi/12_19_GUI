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

    // --- モード管理 ---
    private GalleryViewMode _currentMode = GalleryViewMode.List;
    public GalleryViewMode CurrentMode
    {
        get => _currentMode;
        set { _currentMode = value; RaisePropertyChanged(); }
    }

    // 削除モードかどうか
    private bool _isDeleteMode = false;
    public bool IsDeleteMode
    {
        get => _isDeleteMode;
        set 
        { 
            _isDeleteMode = value; 
            RaisePropertyChanged(); 
            // モード切替時に各アイテムの表示状態を更新
            UpdateItemsDeleteMode();
            // 右上のボタンテキスト更新
            RaisePropertyChanged(nameof(DeleteButtonText));
            RaisePropertyChanged(nameof(StatusMessage));
        }
    }

    // 削除確認ダイアログの表示
    private bool _showDeleteConfirm = false;
    public bool ShowDeleteConfirm
    {
        get => _showDeleteConfirm;
        set { _showDeleteConfirm = value; RaisePropertyChanged(); }
    }

    public string DeleteButtonText => IsDeleteMode ? "実行" : "削除";
    public string StatusMessage => IsDeleteMode ? "削除する画像を選択してください" : "条件指定";

    // 詳細表示用
    private GalleryDetailViewModel? _detailViewModel;
    public GalleryDetailViewModel? DetailViewModel
    {
        get => _detailViewModel;
        set { _detailViewModel = value; RaisePropertyChanged(); }
    }

    // フィルタリング用
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

    // --- コマンド ---
    public ICommand HeaderDeleteButtonCommand { get; } // 右上の削除ボタン
    public ICommand ExecuteDeleteConfirmCommand { get; } // ダイアログの「はい」
    public ICommand CancelDeleteConfirmCommand { get; }  // ダイアログの「いいえ」
    
    public ICommand ShowDetailCommand { get; }
    public ICommand BackToListCommand { get; }
    public ICommand BackCommand { get; } // 右下の戻る

    public GalleryViewModel(MainViewModel main)
    {
        _main = main;
        _dbService = new DatabaseService();
        
        // 戻るボタン (右下)
        BackCommand = new RelayCommand(() => 
        {
            if (IsDeleteMode)
            {
                // 削除モード中の場合は中断して通常モードへ
                QuitDeleteMode();
            }
            else
            {
                // 通常時はホームへ
                _main.Navigate(new HomeViewModel(_main));
            }
        });

        BackToListCommand = new RelayCommand(ExecuteBackToList);
        ShowDetailCommand = new RelayCommand<InspectionRecord>(ExecuteShowDetail);

        // 右上の削除ボタン
        HeaderDeleteButtonCommand = new RelayCommand(() =>
        {
            if (!IsDeleteMode)
            {
                // モード開始
                IsDeleteMode = true;
            }
            else
            {
                // 実行ボタンとして機能 -> 確認ダイアログへ
                // 選択されている数を確認
                int count = CountSelectedItems();
                if (count > 0)
                {
                    ShowDeleteConfirm = true;
                }
                else
                {
                    // 0個選択ならモード終了でいいか、何もしないか。ここでは終了扱いにする
                    QuitDeleteMode();
                }
            }
        });

        // 削除実行 (はい)
        ExecuteDeleteConfirmCommand = new RelayCommand(PerformDeletion);

        // 削除キャンセル (いいえ)
        CancelDeleteConfirmCommand = new RelayCommand(() =>
        {
            ShowDeleteConfirm = false;
            QuitDeleteMode(); // メイン画面に戻る
        });

        LoadData();
    }

    private void UpdateItemsDeleteMode()
    {
        foreach (var group in DisplayGroups)
        {
            foreach (var item in group.Items)
            {
                item.IsDeleteMode = IsDeleteMode;
                if (!IsDeleteMode) item.IsSelected = false; // モード終了時は選択解除
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

    private void QuitDeleteMode()
    {
        ShowDeleteConfirm = false;
        IsDeleteMode = false;
    }

    private void PerformDeletion()
    {
        // 選択されたアイテムを収集
        var itemsToDelete = new List<GalleryItemViewModel>();
        foreach (var group in DisplayGroups)
        {
            foreach (var item in group.Items)
            {
                if (item.IsSelected) itemsToDelete.Add(item);
            }
        }

        // 削除処理
        foreach (var item in itemsToDelete)
        {
            // 1. DBから削除
            _dbService.DeleteInspection(item.Record.Id);

            // 2. 物理ファイル(ディレクトリ)削除
            // save_absolute_path はディレクトリパス (/home/shikoku-pc/pic/NAME) を指している前提
            if (Directory.Exists(item.Record.SaveAbsolutePath))
            {
                try
                {
                    Directory.Delete(item.Record.SaveAbsolutePath, true); // trueで中身ごと削除
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"File Delete Error: {ex.Message}");
                }
            }
        }

        // 完了処理
        ShowDeleteConfirm = false;
        IsDeleteMode = false;
        
        // データ再読み込み
        LoadData();
    }

    private void ExecuteShowDetail(InspectionRecord? record)
    {
        if (IsDeleteMode) return; // 削除モード中は詳細に飛ばない

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
        // 現在の選択状態やモードを一度リセットする
        DisplayGroups.Clear();
        ExecuteBackToList();
        IsDeleteMode = false; 

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
    
    // --- 削除モード用 ---
    private bool _isDeleteMode = false;
    public bool IsDeleteMode
    {
        get => _isDeleteMode;
        set { _isDeleteMode = value; RaisePropertyChanged(); }
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
        
        // クリック時の挙動: 削除モードなら選択トグル、通常なら詳細表示
        ItemClickCommand = new RelayCommand(() => 
        {
            if (parentVM.IsDeleteMode)
            {
                IsSelected = !IsSelected;
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
                Thumbnail = new Bitmap(record.ThumbnailPath);
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

        if (record.Type == 1) // 精密
        {
            omotePath = record.PrecisionPcbOmotePath;
            uraPath = record.PrecisionPcbUraPath;
            circuitOmotePath = record.PrecisionCircuitOmotePath;
            circuitUraPath = record.PrecisionCircuitUraPath;
        }
        else // 簡易
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
