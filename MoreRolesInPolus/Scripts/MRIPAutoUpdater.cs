/**
 * @file MRIPAutoUpdater.cs
 * @brief MRIPアドオンの自動更新機能
 * @details
 * - GitHub Releases APIを使って最新バージョンをチェック
 * - 自動更新モード: 安定版/スナップショット
 * - MainMenu表示時に自動更新を実行
 */

using System;
using System.Collections;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Nebula.Modules;
using Nebula.Modules.MetaWidget;
using Nebula.Modules.GUIWidget;
using Nebula.Utilities;
using UnityEngine;
using Virial.Compat;
using Virial.Media;
using Virial.Text;
using Virial.Runtime;

namespace MoreRolesInPolus;

/// <summary>
/// MRIP自動更新マネージャー
/// MainMenuManager.Awake後に自動更新チェックを実行
/// </summary>
public static class MRIPAutoUpdater
{
    /// <summary>
    /// GitHubリポジトリ情報
    /// </summary>
    private const string GitHubOwner = "10-ui";
    private const string GitHubRepo = "MoreRolesInPolus";
    private const string GitHubApiUrl = "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";
    
    /// <summary>
    /// 設定保存用（遅延初期化）
    /// </summary>
    private static JsonDataSaver<AutoUpdateConfig>? _configSaver = null;
    
    /// <summary>
    /// ConfigSaverを安全に取得（初期化失敗時はデフォルト値を返す）
    /// </summary>
    private static JsonDataSaver<AutoUpdateConfig>? ConfigSaver
    {
        get
        {
            if (_configSaver == null)
            {
                try
                {
                    _configSaver = new JsonDataSaver<AutoUpdateConfig>("MRIPAutoUpdate");
                }
                catch (Exception ex)
                {
                    NebulaPlugin.Log.Print(NebulaLog.LogLevel.Warning, MRIPInfo.LogPrefix($"Failed to initialize ConfigSaver: {ex.Message}"));
                    // 初期化失敗時はnullを返し、呼び出し側で対処
                    return null;
                }
            }
            return _configSaver;
        }
    }
    
    /// <summary>
    /// 自動更新モード
    /// </summary>
    public enum AutoUpdateMode
    {
        /// <summary>無効</summary>
        Disabled,
        /// <summary>最新の安定版</summary>
        Major,
        /// <summary>最新のスナップショット</summary>
        Snapshot
    }
    
    /// <summary>
    /// 現在の自動更新モードを取得
    /// </summary>
    /// <returns>自動更新モード</returns>
    public static AutoUpdateMode GetAutoUpdateMode()
    {
        var saver = ConfigSaver;
        if (saver == null)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Warning, MRIPInfo.LogPrefix("ConfigSaver unavailable, returning Disabled"));
            return AutoUpdateMode.Disabled;
        }
        return saver.Data.Mode;
    }
    
    /// <summary>
    /// 自動更新モードを設定
    /// </summary>
    /// <param name="mode">設定するモード</param>
    public static void SetAutoUpdateMode(AutoUpdateMode mode)
    {
        var saver = ConfigSaver;
        if (saver == null)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Warning, MRIPInfo.LogPrefix($"ConfigSaver unavailable, cannot save mode: {mode}"));
            return;
        }
        saver.Data.Mode = mode;
        saver.Save();
        NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Auto-update mode set to: {mode}"));
    }
    
    /// <summary>
    /// 既にチェック済みかどうか（セッション中に1回だけチェック）
    /// </summary>
    private static bool HasCheckedThisSession = false;
    
    /// <summary>
    /// MainMenu表示後に呼ばれる自動更新チェック
    /// </summary>
    public static void OnMainMenuLoaded()
    {
        if (HasCheckedThisSession)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("Update check already done this session."));
            return;
        }
        
        HasCheckedThisSession = true;
        
        try
        {
            // 前回削除できなかったファイルをクリーンアップ
            MRIPModUpdater.ReleasedInfo.CleanupPendingDeleteFiles();
            
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("MainMenu loaded, checking for updates..."));
            
            var mode = GetAutoUpdateMode();
            if (mode != AutoUpdateMode.Disabled)
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Auto-update mode: {mode}, starting update check..."));
                CheckForUpdatesWithMode(mode);
            }
            else
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("Auto-update is disabled."));
            }
        }
        catch (Exception ex)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix($"Auto-updater error: {ex.Message}\n{ex.StackTrace}"));
        }
    }
    
    /// <summary>
    /// 指定モードで更新をチェック
    /// </summary>
    /// <param name="mode">自動更新モード</param>
    private static void CheckForUpdatesWithMode(AutoUpdateMode mode)
    {
        NebulaManager.Instance.StartCoroutine(CoCheckForUpdatesWithMode(mode).WrapToIl2Cpp());
    }
    
    /// <summary>
    /// 更新チェックのコルーチン
    /// </summary>
    private static IEnumerator CoCheckForUpdatesWithMode(AutoUpdateMode mode)
    {
        NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("Fetching releases from GitHub..."));
        
        // バージョン一覧を取得
        MRIPModUpdater.ResetCache();
        yield return MRIPModUpdater.CoFetchVersionTags((releases) => { });
        
        var cache = MRIPModUpdater.Cache;
        if (cache == null || cache.Count == 0)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix("Failed to fetch releases."));
            yield break;
        }
        
        // モードに応じた最新バージョンを探す
        MRIPModUpdater.ReleasedInfo? targetRelease = null;
        MRIPModUpdater.ReleaseCategory targetCategory = mode == AutoUpdateMode.Major 
            ? MRIPModUpdater.ReleaseCategory.Major 
            : MRIPModUpdater.ReleaseCategory.Snapshot;
        
        foreach (var release in cache)
        {
            if (release.Category == targetCategory)
            {
                targetRelease = release;
                break; // ソート済みなので最初のものが最新
            }
        }
        
        if (targetRelease == null)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"No {targetCategory} releases found."));
            yield break;
        }
        
        NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Latest {targetCategory}: {targetRelease.DisplayVersion}"));
        
        // 現在のバージョンと比較
        if (targetRelease.IsCurrentVersion())
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("Already up to date."));
            yield break;
        }
        
        // 更新が必要か確認
        if (!IsNewerVersion(MRIPInfo.Version, targetRelease.VersionForCompare, mode))
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("No newer version available."));
            yield break;
        }
        
        NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"New version available: {targetRelease.DisplayVersion}"));
        
        // ダウンロード確認ウィンドウを表示
        ShowUpdateConfirmation(targetRelease);
    }
    
    /// <summary>
    /// 最新バージョンの方が新しいかどうかを判定
    /// </summary>
    /// <param name="current">現在のバージョン</param>
    /// <param name="latest">最新のバージョン</param>
    /// <param name="mode">自動更新モード</param>
    /// <returns>最新版が新しければtrue</returns>
    private static bool IsNewerVersion(string current, string latest, AutoUpdateMode mode)
    {
        if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(latest)) return false;
        if (current == latest) return false;
        
        bool currentIsSnapshot = current.StartsWith("Snapshot");
        bool latestIsSnapshot = latest.StartsWith("Snapshot");
        
        // 両方Snapshotの場合: 日付とサフィックスで比較
        if (currentIsSnapshot && latestIsSnapshot)
        {
            // "Snapshot_26.01.05a" → "26.01.05a"
            string currentDate = current.Contains("_") ? current.Substring(current.IndexOf('_') + 1) : current;
            string latestDate = latest.Contains("_") ? latest.Substring(latest.IndexOf('_') + 1) : latest;
            return string.Compare(latestDate, currentDate, StringComparison.Ordinal) > 0;
        }
        
        // 両方正式版の場合: セマンティックバージョニングで比較
        if (!currentIsSnapshot && !latestIsSnapshot)
        {
            try
            {
                var currentVer = new System.Version(current);
                var latestVer = new System.Version(latest);
                return latestVer > currentVer;
            }
            catch
            {
                return string.Compare(latest, current, StringComparison.Ordinal) > 0;
            }
        }
        
        // 異なるカテゴリ間
        if (mode == AutoUpdateMode.Major)
        {
            // 安定版モードでは、現在がSnapshotでも安定版への更新を許可
            return !latestIsSnapshot;
        }
        else
        {
            // スナップショットモードでは常に最新のスナップショットを優先
            return latestIsSnapshot;
        }
    }
    
    /// <summary>
    /// 更新確認ウィンドウを表示
    /// </summary>
    private static void ShowUpdateConfirmation(MRIPModUpdater.ReleasedInfo release)
    {
        MetaScreen window = MetaScreen.GenerateWindow(
            new UnityEngine.Vector2(4.5f, 2.5f),
            DestroyableSingleton<HudManager>.InstanceExists ? DestroyableSingleton<HudManager>.Instance.transform : null,
            UnityEngine.Vector3.zero,
            true, true, true, BackgroundSetting.Old, true
        );
        
        Virial.Compat.Size size;
        window.SetWidget(
            NebulaGUIWidgetEngine.API.VerticalHolder(GUIAlignment.Center, new GUIWidget[]
            {
                new NoSGUIText(GUIAlignment.Center, NebulaGUIWidgetEngine.API.GetAttribute(AttributeAsset.OverlayTitle), 
                    new RawTextComponent($"{MRIPInfo.AddonName}の更新")),
                
                NebulaGUIWidgetEngine.API.VerticalMargin(0.15f),
                
                new NoSGUIText(GUIAlignment.Center, NebulaGUIWidgetEngine.API.GetAttribute(AttributeAsset.OverlayContent), 
                    new RawTextComponent($"現在: {MRIPInfo.Version}\n最新: {release.DisplayVersion}")),
                
                NebulaGUIWidgetEngine.API.VerticalMargin(0.15f),
                
                new NoSGUIText(GUIAlignment.Center, NebulaGUIWidgetEngine.API.GetAttribute(AttributeAsset.OverlayContent), 
                    NebulaGUIWidgetEngine.API.ColorTextComponent(new Virial.Color(UnityEngine.Color.yellow), new RawTextComponent("更新をダウンロードしますか？"))),
                
                NebulaGUIWidgetEngine.API.VerticalMargin(0.2f),
                
                NebulaGUIWidgetEngine.API.HorizontalHolder(GUIAlignment.Center, new GUIWidget[]
                {
                    NebulaGUIWidgetEngine.API.Button(GUIAlignment.Center, NebulaGUIWidgetEngine.API.GetAttribute(AttributeAsset.OptionsButton), 
                        new RawTextComponent("キャンセル"), 
                        (GUIClickable _) => { window.CloseScreen(); }, 
                        null, null, null, null, null, null),
                    
                    new NoSGUIMargin(GUIAlignment.Center, new UnityEngine.Vector2(0.2f, 0f)),
                    
                    NebulaGUIWidgetEngine.API.Button(GUIAlignment.Center, NebulaGUIWidgetEngine.API.GetAttribute(AttributeAsset.OptionsButton), 
                        new RawTextComponent("ダウンロード"), 
                        (GUIClickable _) =>
                        {
                            window.CloseScreen();
                            NebulaManager.Instance.StartCoroutine(release.CoUpdateAndShowDialog().WrapToIl2Cpp());
                        }, 
                        null, null, null, null, null, null)
                })
            }),
            new UnityEngine.Vector2(0.5f, 0.5f),
            out size
        );
    }
    
    /// <summary>
    /// 自動更新設定クラス
    /// </summary>
    public class AutoUpdateConfig
    {
        /// <summary>自動更新モード（デフォルト: 無効）</summary>
        public AutoUpdateMode Mode { get; set; } = AutoUpdateMode.Disabled;
        
        /// <summary>
        /// デフォルトコンストラクタ（JSONデシリアライズに必要）
        /// </summary>
        public AutoUpdateConfig()
        {
        }
    }
}
