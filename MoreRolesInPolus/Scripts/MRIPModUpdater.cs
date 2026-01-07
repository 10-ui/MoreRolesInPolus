/**
 * @file MRIPModUpdater.cs
 * @brief MRIPアドオンのバージョン一覧取得・更新機能
 * @details
 * - GitHub Releases APIからバージョン一覧を取得
 * - タグ形式: "v,v0.1.6"（安定版）、"s,Snapshot_26.01.05c"（スナップショット）
 * - バージョン選択UIに使用されるデータを管理
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Nebula.Modules;
using Nebula.Modules.MetaWidget;
using Nebula.Modules.GUIWidget;
using Nebula.Utilities;
using UnityEngine;
using Virial.Media;
using Virial.Text;

namespace MoreRolesInPolus;

/// <summary>
/// MRIPバージョン更新マネージャー
/// </summary>
public static class MRIPModUpdater
{
    /// <summary>
    /// GitHubリポジトリ情報
    /// </summary>
    private const string GitHubOwner = "10-ui";
    private const string GitHubRepo = "MoreRolesInPolus";
    
    /// <summary>
    /// ページあたりの取得数
    /// </summary>
    private const int PerPage = 30;
    
    /// <summary>
    /// GitHub APIエンドポイント
    /// </summary>
    /// <param name="page">ページ番号</param>
    /// <returns>API URL</returns>
    private static string GetReleasesUrl(int page) => $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases?per_page={PerPage}&page={page}";
    
    /// <summary>
    /// 次に取得するページ番号
    /// </summary>
    private static int NextPage = 1;
    
    /// <summary>
    /// キャッシュされたバージョン一覧
    /// </summary>
    private static List<ReleasedInfo>? _cache = null;
    
    /// <summary>
    /// キャッシュされたバージョン一覧（読み取り専用）
    /// </summary>
    public static List<ReleasedInfo>? Cache => _cache;
    
    /// <summary>
    /// これ以上ページがないかどうか
    /// </summary>
    public static bool MaybeNoMorePages { get; private set; } = false;
    
    /// <summary>
    /// キャッシュをリセット
    /// </summary>
    public static void ResetCache()
    {
        _cache = null;
        NextPage = 1;
        MaybeNoMorePages = false;
    }
    
    /// <summary>
    /// アドオン読み込み直後に古いMRIPファイルを削除
    /// この時点で古いファイルは読み込みスキップされてDispose()済み = ロック解除済み
    /// </summary>
    public static void CleanupOldAddonFiles()
    {
        try
        {
            string addonsPath = PathHelpers.GameRootPath + Path.DirectorySeparatorChar + "Addons";
            if (!Directory.Exists(addonsPath)) return;
            
            // 現在読み込まれているMRIPアドオンの情報を取得
            var currentAddon = MRIPInfo.Addon;
            if (currentAddon == null)
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Warning, MRIPInfo.LogPrefix("CleanupOldAddonFiles: Current addon not found"));
                return;
            }
            
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"CleanupOldAddonFiles: Starting cleanup... (AddonId={MRIPInfo.AddonId})"));
            
            // MRIPに関連する全ファイル（.zip, .old）を検索
            var filesToDelete = new List<string>();
            
            foreach (string file in Directory.GetFiles(addonsPath))
            {
                string fileName = Path.GetFileName(file);
                string ext = Path.GetExtension(file).ToLower();
                
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"CleanupOldAddonFiles: Checking file: {fileName}"));
                
                // .zipまたは.oldファイルで、MRIPのIDを含むもの
                if ((ext == ".zip" || ext == ".old") && fileName.Contains(MRIPInfo.AddonId))
                {
                    NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"CleanupOldAddonFiles: Matched MRIP file: {fileName}"));
                    filesToDelete.Add(file);
                }
            }
            
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"CleanupOldAddonFiles: Found {filesToDelete.Count} MRIP files"));
            
            if (filesToDelete.Count <= 1)
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("CleanupOldAddonFiles: No old files to delete (only 0-1 file found)"));
                return;
            }
            
            // !の数でソート（多い順）、最初のファイル（最優先で読み込まれたもの）は残す
            filesToDelete.Sort((a, b) =>
            {
                int countA = Path.GetFileName(a).TakeWhile(c => c == '!').Count();
                int countB = Path.GetFileName(b).TakeWhile(c => c == '!').Count();
                return countB.CompareTo(countA); // 多い順
            });
            
            string currentFile = filesToDelete[0];
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"CleanupOldAddonFiles: Keeping current file: {Path.GetFileName(currentFile)}"));
            
            // 2番目以降を削除
            for (int i = 1; i < filesToDelete.Count; i++)
            {
                string file = filesToDelete[i];
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"CleanupOldAddonFiles: Attempting to delete: {Path.GetFileName(file)}"));
                try
                {
                    File.Delete(file);
                    NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"CleanupOldAddonFiles: SUCCESS - Deleted: {Path.GetFileName(file)}"));
                }
                catch (Exception ex)
                {
                    NebulaPlugin.Log.Print(NebulaLog.LogLevel.Warning, MRIPInfo.LogPrefix($"CleanupOldAddonFiles: FAILED - {Path.GetFileName(file)}: {ex.Message}"));
                }
            }
        }
        catch (Exception ex)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix($"CleanupOldAddonFiles error: {ex.Message}"));
        }
    }
    
    /// <summary>
    /// リリースカテゴリ
    /// </summary>
    public enum ReleaseCategory
    {
        /// <summary>安定版</summary>
        Major,
        /// <summary>スナップショット</summary>
        Snapshot,
        /// <summary>不明</summary>
        Unknown
    }
    
    /// <summary>
    /// カテゴリごとの表示色
    /// </summary>
    public static readonly UnityEngine.Color[] CategoryColors = {
        new UnityEngine.Color(176f / 255f, 204f / 255f, 251f / 255f),  // Major: 青系
        new UnityEngine.Color(247f / 255f, 255f / 255f, 29f / 255f),   // Snapshot: 黄色
        new UnityEngine.Color(141f / 255f, 141f / 255f, 141f / 255f)   // Unknown: グレー
    };
    
    /// <summary>
    /// カテゴリごとの翻訳キー（日本語直接指定）
    /// </summary>
    public static readonly string[] CategoryNames = {
        "安定版",
        "スナップショット",
        "不明"
    };
    
    /// <summary>
    /// リリース情報クラス
    /// </summary>
    public class ReleasedInfo
    {
        /// <summary>カテゴリ</summary>
        public ReleaseCategory Category { get; private set; }
        
        /// <summary>表示用バージョン名（例: "v0.1.6", "Snapshot 26.01.05c"）</summary>
        public string DisplayVersion { get; private set; }
        
        /// <summary>GitHubのタグ名（例: "v,v0.1.6", "s,Snapshot_26.01.05c"）</summary>
        public string RawTag { get; private set; }
        
        /// <summary>リリースボディ（説明文）</summary>
        public string? Body { get; private set; }
        
        /// <summary>ダウンロードURL（.zipファイル）</summary>
        public string? DownloadUrl { get; private set; }
        
        /// <summary>addon.metaのVersionと比較用の値</summary>
        public string VersionForCompare { get; private set; }
        
        /// <summary>ファイル名用のバージョン文字列（例: "v0.1.6", "Snapshot_26.01.05c"）</summary>
        public string VersionForFileName { get; private set; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="tag">GitHubタグ名</param>
        /// <param name="body">リリース説明文</param>
        /// <param name="downloadUrl">ダウンロードURL</param>
        public ReleasedInfo(string tag, string? body, string? downloadUrl)
        {
            RawTag = tag;
            Body = body;
            DownloadUrl = downloadUrl;
            
            // タグをパース
            // 形式: "v,v0.1.6" または "s,Snapshot_26.01.05c"
            string[] parts = tag.Split(',');
            
            if (parts.Length >= 2)
            {
                string typePrefix = parts[0];
                string version = parts[1];
                
                switch (typePrefix)
                {
                    case "v":
                        Category = ReleaseCategory.Major;
                        // "v0.1.6" → "v0.1.6"（そのまま）
                        DisplayVersion = version;
                        // 比較用: "0.1.6"（vを除去）
                        VersionForCompare = version.StartsWith("v") ? version.Substring(1) : version;
                        // ファイル名用: "v0.1.6"（そのまま）
                        VersionForFileName = version;
                        break;
                        
                    case "s":
                        Category = ReleaseCategory.Snapshot;
                        // "Snapshot_26.01.05c" → "Snapshot 26.01.05c"（アンダースコアをスペースに）
                        DisplayVersion = version.Replace('_', ' ');
                        // 比較用: そのまま
                        VersionForCompare = version;
                        // ファイル名用: "Snapshot_26.01.05c"（そのまま、アンダースコア維持）
                        VersionForFileName = version;
                        break;
                        
                    default:
                        Category = ReleaseCategory.Unknown;
                        DisplayVersion = version;
                        VersionForCompare = version;
                        VersionForFileName = version;
                        break;
                }
            }
            else
            {
                // パース失敗
                Category = ReleaseCategory.Unknown;
                DisplayVersion = tag;
                VersionForCompare = tag;
                VersionForFileName = tag;
            }
        }
        
        /// <summary>
        /// 現在インストールされているバージョンかどうか
        /// </summary>
        /// <returns>現在のバージョンならtrue</returns>
        public bool IsCurrentVersion()
        {
            string currentVersion = MRIPInfo.Version;
            return currentVersion == VersionForCompare;
        }
        
        /// <summary>
        /// 更新をダウンロードしてインストール
        /// </summary>
        /// <returns>コルーチン</returns>
        public IEnumerator CoUpdateAndShowDialog()
        {
            if (string.IsNullOrEmpty(DownloadUrl))
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix("Download URL is empty."));
                yield break;
            }
            
            // ダウンロード中のウィンドウを表示
            MetaScreen downloadWindow = MetaScreen.GenerateWindow(
                new UnityEngine.Vector2(3.5f, 1.5f),
                DestroyableSingleton<HudManager>.InstanceExists ? DestroyableSingleton<HudManager>.Instance.transform : null,
                UnityEngine.Vector3.zero,
                true, false, false, BackgroundSetting.Old, false
            );
            
            Virial.Compat.Size size;
            downloadWindow.SetWidget(
                NebulaGUIWidgetEngine.API.VerticalHolder(GUIAlignment.Center, new GUIWidget[]
                {
                    new GUILoadingIcon(GUIAlignment.Center) { Size = 0.35f },
                    NebulaGUIWidgetEngine.API.VerticalMargin(0.1f),
                    new NoSGUIText(GUIAlignment.Center, NebulaGUIWidgetEngine.API.GetAttribute(AttributeAsset.OverlayContent), 
                        new RawTextComponent("更新をダウンロード中..."))
                }),
                new UnityEngine.Vector2(0.5f, 0.5f),
                out size
            );
            
            // ダウンロード実行
            bool success = false;
            string errorMessage = "";
            
            yield return CoDownloadAndInstall(DownloadUrl, (result, error) =>
            {
                success = result;
                errorMessage = error;
            });
            
            downloadWindow.CloseScreen();
            
            // 結果表示
            if (success)
            {
                string message = $"更新のダウンロードが完了しました。\n\nゲームを再起動すると、\n新しいバージョン({DisplayVersion})が適用されます。";
                
                // 削除できなかったファイルがあれば通知（次回起動で自動削除されるはずなので簡潔に）
                if (LastFailedToDeleteFiles.Count > 0)
                {
                    message += $"\n\n<color=yellow>※ 古いファイルは次回起動時に自動削除されます</color>";
                }
                
                ShowMessage("ダウンロード完了", message);
            }
            else
            {
                ShowMessage("ダウンロード失敗", errorMessage);
            }
        }
        
        /// <summary>
        /// 削除に失敗したファイル（メッセージ表示用）
        /// </summary>
        private static List<string> LastFailedToDeleteFiles = new List<string>();
        
        /// <summary>
        /// ダウンロードとインストールを実行
        /// </summary>
        private IEnumerator CoDownloadAndInstall(string url, Action<bool, string> callback)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Downloading from: {url}"));
            
            HttpClient client = GetHttpClient();
            HttpResponseMessage? response = null;
            
            // ダウンロード
            var downloadTask = client.GetAsync(url);
            while (!downloadTask.IsCompleted)
            {
                yield return null;
            }
            
            try
            {
                response = downloadTask.Result;
            }
            catch (Exception ex)
            {
                callback(false, $"ダウンロードリクエスト失敗:\n{ex.Message}");
                yield break;
            }
            
            if (!response.IsSuccessStatusCode)
            {
                callback(false, $"ダウンロード失敗\nステータス: {response.StatusCode}");
                response?.Dispose();
                yield break;
            }
            
            // ファイル読み取り
            var contentTask = response.Content.ReadAsByteArrayAsync();
            while (!contentTask.IsCompleted)
            {
                yield return null;
            }
            
            byte[] zipData;
            try
            {
                zipData = contentTask.Result;
            }
            catch (Exception ex)
            {
                callback(false, $"データ読み取り失敗:\n{ex.Message}");
                response?.Dispose();
                yield break;
            }
            
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Downloaded {zipData.Length} bytes."));
            
            // Addonsフォルダに保存
            try
            {
                string addonsPath = PathHelpers.GameRootPath + Path.DirectorySeparatorChar + "Addons";
                
                // 古いMRIPのzipファイルを削除
                LastFailedToDeleteFiles.Clear();
                DeleteOldAddonFiles(addonsPath, LastFailedToDeleteFiles);
                
                // 新しいファイル名（バージョン情報付き）
                // Nebulaは Directory.GetFiles の順序でアドオンを読み込み、同じIDは最初のものだけ登録される
                // !!xxx < !Mxxx (ASCIIで ! < M) なので、!を増やすほど先に読み込まれる
                // 既存ファイルより多くの!を付けて、常に新しいダウンロードが優先されるようにする
                string prefix = GetNextPriorityPrefix(addonsPath);
                string zipPath = Path.Combine(addonsPath, $"{prefix}{MRIPInfo.AddonId}-{VersionForFileName}.zip");
                
                File.WriteAllBytes(zipPath, zipData);
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Update saved to: {zipPath}"));
                
                callback(true, "");
            }
            catch (Exception ex)
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix($"Failed to save file: {ex.Message}"));
                callback(false, $"ファイル保存失敗:\n{ex.Message}");
            }
            
            response?.Dispose();
        }
        
        /// <summary>
        /// 削除予定ファイルのパス
        /// </summary>
        private static string GetPendingDeleteFilePath()
        {
            return Path.Combine(PathHelpers.GameRootPath, "Addons", "MRIPPendingDelete.txt");
        }
        
        /// <summary>
        /// 次のダウンロードで使用する優先度プレフィックスを取得
        /// 既存のMRIPファイルより多くの!を付けて、常に新しいダウンロードが最優先で読み込まれるようにする
        /// </summary>
        /// <param name="addonsPath">Addonsフォルダのパス</param>
        /// <returns>プレフィックス文字列（例: "!", "!!", "!!!"）</returns>
        private static string GetNextPriorityPrefix(string addonsPath)
        {
            int maxExclamations = 0;
            
            try
            {
                if (Directory.Exists(addonsPath))
                {
                    string[] allZipFiles = Directory.GetFiles(addonsPath, "*.zip");
                    foreach (string file in allZipFiles)
                    {
                        string fileName = Path.GetFileName(file);
                        if (fileName.Contains(MRIPInfo.AddonId))
                        {
                            // 先頭の!の数をカウント
                            int count = 0;
                            foreach (char c in fileName)
                            {
                                if (c == '!') count++;
                                else break;
                            }
                            if (count > maxExclamations) maxExclamations = count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Warning, MRIPInfo.LogPrefix($"Error getting priority prefix: {ex.Message}"));
            }
            
            // 既存の最大値より1つ多い!を返す（最低1つ）
            return new string('!', maxExclamations + 1);
        }
        
        /// <summary>
        /// 古いアドオンファイルを削除（または.oldにリネーム）
        /// </summary>
        /// <param name="addonsPath">Addonsフォルダのパス</param>
        /// <param name="failedFiles">削除に失敗したファイルのリスト（出力用）</param>
        private static void DeleteOldAddonFiles(string addonsPath, List<string> failedFiles)
        {
            try
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"DeleteOldAddonFiles: Checking path: {addonsPath}"));
                
                if (!Directory.Exists(addonsPath))
                {
                    NebulaPlugin.Log.Print(NebulaLog.LogLevel.Warning, MRIPInfo.LogPrefix($"DeleteOldAddonFiles: Directory does not exist: {addonsPath}"));
                    return;
                }
                
                // まず既存の.oldファイルを削除（前回の残り）
                foreach (string oldFile in Directory.GetFiles(addonsPath, "*.old"))
                {
                    if (Path.GetFileName(oldFile).Contains(MRIPInfo.AddonId))
                    {
                        try
                        {
                            File.Delete(oldFile);
                            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Deleted old backup file: {oldFile}"));
                        }
                        catch { /* 無視 */ }
                    }
                }
                
                // ファイル名に "MoreRolesInPolus" が含まれる .zip ファイルを検索して削除
                string[] allZipFiles = Directory.GetFiles(addonsPath, "*.zip");
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"DeleteOldAddonFiles: Found {allZipFiles.Length} zip files"));
                
                foreach (string file in allZipFiles)
                {
                    string fileName = Path.GetFileName(file);
                    
                    if (fileName.Contains(MRIPInfo.AddonId))
                    {
                        NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"DeleteOldAddonFiles: Processing file: {fileName}"));
                        
                        // 1. まず削除を試みる
                        try
                        {
                            File.Delete(file);
                            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Deleted old addon file: {file}"));
                            continue;
                        }
                        catch (Exception ex)
                        {
                            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Delete failed, trying rename: {ex.Message}"));
                        }
                        
                        // 2. 削除が失敗したらリネームを試みる（Nebula本体と同じ方式）
                        try
                        {
                            string oldPath = file + ".old";
                            File.Move(file, oldPath, true);
                            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Renamed old addon file to: {oldPath}"));
                            continue;
                        }
                        catch (Exception ex)
                        {
                            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Warning, MRIPInfo.LogPrefix($"Rename also failed: {ex.Message}"));
                            failedFiles.Add(fileName);
                        }
                    }
                }
                
                // 削除もリネームも失敗したファイルがあれば、次回起動時に削除するためにマークする
                if (failedFiles.Count > 0)
                {
                    string pendingFile = GetPendingDeleteFilePath();
                    var fullPaths = failedFiles.Select(f => Path.Combine(addonsPath, f)).ToList();
                    File.WriteAllLines(pendingFile, fullPaths);
                    NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Marked {failedFiles.Count} files for deletion on next startup"));
                }
            }
            catch (Exception ex)
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Warning, MRIPInfo.LogPrefix($"Error cleaning old files: {ex.Message}"));
            }
        }
        
        /// <summary>
        /// 起動時に削除予定のファイルを削除する（アドオン読み込み前に呼ばれる必要がある）
        /// </summary>
        public static void CleanupPendingDeleteFiles()
        {
            try
            {
                string pendingFile = GetPendingDeleteFilePath();
                if (!File.Exists(pendingFile)) return;
                
                string[] filesToDelete = File.ReadAllLines(pendingFile);
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"CleanupPendingDeleteFiles: Found {filesToDelete.Length} files to delete"));
                
                foreach (string file in filesToDelete)
                {
                    if (string.IsNullOrWhiteSpace(file)) continue;
                    
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Deleted pending file: {file}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        NebulaPlugin.Log.Print(NebulaLog.LogLevel.Warning, MRIPInfo.LogPrefix($"Failed to delete pending file {file}: {ex.Message}"));
                    }
                }
                
                // マーカーファイルを削除
                File.Delete(pendingFile);
            }
            catch (Exception ex)
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Warning, MRIPInfo.LogPrefix($"Error in CleanupPendingDeleteFiles: {ex.Message}"));
            }
        }
        
        /// <summary>
        /// メッセージウィンドウを表示
        /// </summary>
        private static void ShowMessage(string title, string message)
        {
            // メッセージの長さに応じてウィンドウサイズを調整
            int lineCount = message.Split('\n').Length;
            float height = Math.Max(2.5f, 1.5f + lineCount * 0.25f);
            
            MetaScreen window = MetaScreen.GenerateWindow(
                new UnityEngine.Vector2(5.5f, height),
                DestroyableSingleton<HudManager>.InstanceExists ? DestroyableSingleton<HudManager>.Instance.transform : null,
                UnityEngine.Vector3.zero,
                true, true, true, BackgroundSetting.Old, true
            );
            
            Virial.Compat.Size size;
            window.SetWidget(
                NebulaGUIWidgetEngine.API.VerticalHolder(GUIAlignment.Center, new GUIWidget[]
                {
                    new NoSGUIText(GUIAlignment.Center, NebulaGUIWidgetEngine.API.GetAttribute(AttributeAsset.OverlayTitle), 
                        new RawTextComponent(title)),
                    
                    NebulaGUIWidgetEngine.API.VerticalMargin(0.15f),
                    
                    new NoSGUIText(GUIAlignment.Center, NebulaGUIWidgetEngine.API.GetAttribute(AttributeAsset.OverlayContent), 
                        new RawTextComponent(message)),
                    
                    NebulaGUIWidgetEngine.API.VerticalMargin(0.2f),
                    
                    NebulaGUIWidgetEngine.API.Button(GUIAlignment.Center, NebulaGUIWidgetEngine.API.GetAttribute(AttributeAsset.CenteredBoldFixed), 
                        new RawTextComponent("OK"), 
                        (GUIClickable _) => { window.CloseScreen(); }, 
                        null, null, null, null, null, null)
                }),
                new UnityEngine.Vector2(0.5f, 0.5f),
                out size
            );
        }
    }
    
    /// <summary>
    /// GitHub API レスポンス: リリース情報
    /// </summary>
    private class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
        
        [JsonPropertyName("body")]
        public string? Body { get; set; }
        
        [JsonPropertyName("assets")]
        public List<GitHubAssetResponse>? Assets { get; set; }
    }
    
    /// <summary>
    /// GitHub API レスポンス: アセット情報
    /// </summary>
    private class GitHubAssetResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
    
    /// <summary>
    /// バージョン一覧を取得するコルーチン
    /// </summary>
    /// <param name="postAction">取得完了後のコールバック</param>
    /// <returns>コルーチン</returns>
    public static IEnumerator CoFetchVersionTags(Action<List<ReleasedInfo>> postAction)
    {
        yield return FetchAsync().WaitAsCoroutine();
        postAction.Invoke(_cache ?? new List<ReleasedInfo>());
    }
    
    /// <summary>
    /// HTTPクライアント（User-Agent付き）
    /// </summary>
    private static HttpClient? _httpClient = null;
    
    /// <summary>
    /// HTTPクライアントを取得（User-Agent付き）
    /// </summary>
    private static HttpClient GetHttpClient()
    {
        if (_httpClient == null)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"MoreRolesInPolus/{MRIPInfo.Version}");
        }
        return _httpClient;
    }
    
    /// <summary>
    /// 非同期でバージョン一覧を取得
    /// </summary>
    private static async System.Threading.Tasks.Task FetchAsync()
    {
        List<ReleasedInfo> releases = new List<ReleasedInfo>(_cache ?? new List<ReleasedInfo>());
        
        try
        {
            string url = GetReleasesUrl(NextPage);
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Fetching releases from: {url}"));
            
            var response = await GetHttpClient().GetAsync(url);
            
            if (response.StatusCode == HttpStatusCode.OK)
            {
                string json = await response.Content.ReadAsStringAsync();
                
                int lastCount = releases.Count;
                
                var releaseList = JsonSerializer.Deserialize<List<GitHubReleaseResponse>>(json);
                
                if (releaseList != null)
                {
                    foreach (var release in releaseList)
                    {
                        if (string.IsNullOrEmpty(release.TagName)) continue;
                        
                        // .zipファイルのダウンロードURLを探す
                        string? downloadUrl = null;
                        if (release.Assets != null)
                        {
                            foreach (var asset in release.Assets)
                            {
                                if (asset.Name != null && asset.Name.EndsWith(".zip"))
                                {
                                    downloadUrl = asset.BrowserDownloadUrl;
                                    break;
                                }
                            }
                        }
                        
                        // リリース情報を追加
                        releases.Add(new ReleasedInfo(
                            release.TagName,
                            release.Body?.Replace("\\n", "\n").Replace("\\r", ""),
                            downloadUrl
                        ));
                    }
                }
                
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"{releases.Count} releases fetched from GitHub."));
                
                // 取得数が増えていなければこれ以上ページがない
                if (releases.Count == lastCount)
                {
                    MaybeNoMorePages = true;
                }
                
                NextPage++;
            }
            else
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix($"Failed to fetch releases: {response.StatusCode}"));
                MaybeNoMorePages = true;
            }
            
            response.Dispose();
        }
        catch (Exception ex)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix($"Error fetching releases: {ex.Message}"));
            MaybeNoMorePages = true;
        }
        
        // カテゴリ順、バージョン順でソート（スナップショットは日付文字列で比較）
        releases.Sort((a, b) =>
        {
            // カテゴリが同じ場合はバージョンで比較
            if (a.Category == b.Category)
            {
                // 新しい順（降順）
                return string.Compare(b.VersionForCompare, a.VersionForCompare, StringComparison.Ordinal);
            }
            // Majorを先に
            return a.Category.CompareTo(b.Category);
        });
        
        _cache = releases;
    }
}
