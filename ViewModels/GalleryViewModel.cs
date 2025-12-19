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

    public GalleryDateGroupViewModel(IGrouping<string, InspectionRecord> group, ICommand itemClickCommand)
    {
        if (DateTime.TryParseExact(group.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            DateHeader = date.ToString("M月d日");
        }
        else
        {
            DateHeader = group.Key;
        }

        foreach (var record in group)
        {
            Items.Add(new GalleryItemViewModel(record, itemClickCommand));
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

    public ICommand ShowDetailCommand { get; }
    public ICommand BackToListCommand { get; }
    public ICommand BackCommand { get; }

    public GalleryViewModel(MainViewModel main)
    {
        _main = main;
        _dbService = new DatabaseService();
        
        BackCommand = new RelayCommand(() => _main.Navigate(new HomeViewModel(_main)));
        BackToListCommand = new RelayCommand(ExecuteBackToList);
        ShowDetailCommand = new RelayCommand<InspectionRecord>(ExecuteShowDetail);

        LoadData();
    }

    private void ExecuteShowDetail(InspectionRecord? record)
    {
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
            DisplayGroups.Add(new GalleryDateGroupViewModel(group, ShowDetailCommand));
        }
    }
}

public class GalleryItemViewModel : ViewModelBase
{
    public InspectionRecord Record { get; }
    public Bitmap? Thumbnail { get; }
    public string TypeLabel => Record.Type == 0 ? "簡易" : "精密";
    
    public ICommand ItemClickCommand { get; } 

    public GalleryItemViewModel(InspectionRecord record, ICommand showDetailCommand)
    {
        Record = record;
        ItemClickCommand = new RelayCommand(() => showDetailCommand.Execute(record));
        
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
