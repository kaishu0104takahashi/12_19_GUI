using System;
using System.Collections.Generic;
using System.IO;
using GUI_12_19.Models;
using Microsoft.Data.Sqlite;

namespace GUI_12_19.Services;

public class DatabaseService
{
    private readonly string _databasePath;

    public DatabaseService()
    {
        _databasePath = "/home/shikoku-pc/db/pcb_inspection.db";
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public void Initialize()
    {
        if (!File.Exists(_databasePath))
        {
            try
            {
                using (var connection = new SqliteConnection($"Filename={_databasePath}"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        CREATE TABLE inspection (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            save_name TEXT NOT NULL,
                            save_absolute_path TEXT NOT NULL,
                            date TEXT NOT NULL,
                            type INTEGER NOT NULL 
                        );";
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { Console.WriteLine($"DB Init Error: {ex.Message}"); }
        }
    }

    public List<InspectionRecord> GetAllRecords()
    {
        var records = new List<InspectionRecord>();
        if (!File.Exists(_databasePath)) return records;

        try
        {
            using (var connection = new SqliteConnection($"Filename={_databasePath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT id, save_name, save_absolute_path, date, type FROM inspection ORDER BY date DESC;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        records.Add(new InspectionRecord {
                            Id = reader.GetInt32(0),
                            SaveName = reader.GetString(1),
                            SaveAbsolutePath = reader.GetString(2),
                            Date = reader.GetString(3),
                            Type = reader.GetInt32(4)
                        });
                    }
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"DB Load Error: {ex.Message}"); }
        return records;
    }

    public void InsertInspection(string saveName, string absolutePath, string date, int type)
    {
        try
        {
            using (var connection = new SqliteConnection($"Filename={_databasePath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO inspection (save_name, save_absolute_path, date, type)
                    VALUES (@name, @path, @date, @type);";
                
                command.Parameters.AddWithValue("@name", saveName);
                command.Parameters.AddWithValue("@path", absolutePath);
                command.Parameters.AddWithValue("@date", date);
                command.Parameters.AddWithValue("@type", type);

                command.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DB Insert Error: {ex.Message}");
            throw; 
        }
    }

    // --- 新規追加: 削除機能 ---
    public void DeleteInspection(int id)
    {
        try
        {
            using (var connection = new SqliteConnection($"Filename={_databasePath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM inspection WHERE id = @id;";
                command.Parameters.AddWithValue("@id", id);
                command.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DB Delete Error: {ex.Message}");
            throw;
        }
    }
}
