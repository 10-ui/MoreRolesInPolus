/**
 * @file MRIPSettingsButton.cs
 * @brief Nebulaの設定画面（「ネブラ」ボタン）にMRIPボタンを追加 + 起動時自動更新チェック
 * @details
 * - MainMenuManager.Awake後にNebulaScreenへボタンを追加
 * - 起動時に自動更新チェックを実行
 * - バージョン選択UI（Nebula風）を表示
 */

using HarmonyLib;
using Nebula.Modules;
using Nebula.Modules.GUIWidget;
using Nebula.Modules.MetaWidget;
using Nebula.Patches;
using Virial.Media;
using Virial.Runtime;
using Virial.Text;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace MoreRolesInPolus;

/// <summary>
/// MainMenu表示後にMRIPボタンを追加するHarmonyパッチのセットアップ
/// </summary>
[NebulaPreprocess(PreprocessPhase.PostLoadAddons)]
public static class MRIPMainMenuPatchSetup
{
    private static Harmony? HarmonyInstance;
    
    /// <summary>
    /// Harmonyパッチを適用
    /// </summary>
    /// <param name="preprocessor">プリプロセッサー</param>
    public static void Preprocess(NebulaPreprocessor preprocessor)
    {
        try
        {
            // アドオン読み込み直後に古いMRIPファイルを削除
            // この時点で古いファイルは読み込みスキップされてDispose()済み = ロック解除済み
            MRIPModUpdater.CleanupOldAddonFiles();
            
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("Setting up MainMenu patch..."));
            
            HarmonyInstance = new Harmony("MoreRolesInPolus.MainMenuPatch");
            
            // MainMenuManager.Awakeの後に処理
            var mainMenuAwakeMethod = typeof(MainMenuManager).GetMethod("Awake");
            var postfix = typeof(MRIPMainMenuPatch).GetMethod(nameof(MRIPMainMenuPatch.MainMenuAwakePostfix));
            
            var harmonyMethod = new HarmonyMethod(postfix);
            harmonyMethod.priority = Priority.Last; // Nebulaの処理の後に実行
            
            HarmonyInstance.Patch(mainMenuAwakeMethod, postfix: harmonyMethod);
            
            // ResetScreenパッチも適用
            MRIPMenuClearScreenPatch.Apply(HarmonyInstance);
            
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("MainMenu patch applied successfully!"));
        }
        catch (System.Exception ex)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix($"Failed to setup MainMenu patch: {ex.Message}\n{ex.StackTrace}"));
        }
    }
}

/// <summary>
/// MainMenuManager.ResetScreenのパッチ（MRIP画面を閉じる）
/// </summary>
public static class MRIPMenuClearScreenPatch
{
    private static bool Patched = false;
    
    /// <summary>
    /// パッチを適用
    /// </summary>
    /// <param name="harmony">Harmonyインスタンス</param>
    public static void Apply(Harmony harmony)
    {
        if (Patched) return;
        
        try
        {
            var resetScreenMethod = typeof(MainMenuManager).GetMethod(nameof(MainMenuManager.ResetScreen));
            var postfix = typeof(MRIPMenuClearScreenPatch).GetMethod(nameof(ResetScreenPostfix));
            harmony.Patch(resetScreenMethod, postfix: new HarmonyMethod(postfix));
            Patched = true;
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("ResetScreen patch applied!"));
        }
        catch (System.Exception ex)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix($"Failed to apply ResetScreen patch: {ex.Message}"));
        }
    }
    
    /// <summary>
    /// MRIP設定画面を閉じる
    /// </summary>
    public static void ResetScreenPostfix()
    {
        MRIPMainMenuPatch.MRIPVersionsScreen?.SetActive(false);
    }
}

/// <summary>
/// MainMenu表示後にMRIPボタンを追加するパッチ
/// </summary>
public static class MRIPMainMenuPatch
{
    private static bool ButtonAdded = false;
    
    /// <summary>
    /// MRIPバージョン選択画面
    /// </summary>
    public static GameObject? MRIPVersionsScreen = null;
    
    /// <summary>
    /// MainMenuManagerのインスタンス
    /// </summary>
    private static MainMenuManager? MainMenuInstance = null;
    
    /// <summary>
    /// MainMenuManager.Awake実行後に呼ばれるPostfixパッチ
    /// </summary>
    /// <param name="__instance">MainMenuManagerのインスタンス</param>
    public static void MainMenuAwakePostfix(MainMenuManager __instance)
    {
        try
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("MainMenuAwake Postfix called..."));
            MainMenuInstance = __instance;
            
            // ボタン追加と自動更新チェック
            __instance.StartCoroutine(SetupMRIPButton(__instance).WrapToIl2Cpp());
        }
        catch (System.Exception ex)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix($"MainMenuAwakePostfix error: {ex.Message}\n{ex.StackTrace}"));
        }
    }
    
    /// <summary>
    /// MRIPボタンをセットアップするコルーチン
    /// </summary>
    private static System.Collections.IEnumerator SetupMRIPButton(MainMenuManager mainMenu)
    {
        // UIがロードされるまで待つ
        for (int i = 0; i < 60; i++)
        {
            yield return null;
        }
        
        // 自動更新チェックは無効化
        // NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("Starting auto-update check..."));
        // MRIPAutoUpdater.OnMainMenuLoaded();
        
        // NebulaScreenへのボタン追加を継続的に監視
        // NebulaScreenは「ネブラ」ボタンを押した時に表示されるので、アクティブになったタイミングで追加
        NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("Starting NebulaScreen monitor..."));
        mainMenu.StartCoroutine(MonitorNebulaScreen(mainMenu).WrapToIl2Cpp());
    }
    
    /// <summary>
    /// NebulaScreenを継続的に監視するコルーチン
    /// </summary>
    private static System.Collections.IEnumerator MonitorNebulaScreen(MainMenuManager mainMenu)
    {
        int frameCount = 0;
        const int maxFrames = 36000; // 約10分（60fps × 600秒）
        bool wasNebulaScreenActive = false;
        
        // メインメニューにいる間ずっと監視
        while (frameCount < maxFrames)
        {
            yield return null;
            frameCount++;
            
            GameObject? nebulaScreen = MainMenuSetUpPatch.NebulaScreen;
            bool isNebulaScreenActive = nebulaScreen != null && nebulaScreen.activeInHierarchy;
            
            // NebulaScreenが見つかって、アクティブで、まだボタンを追加していない場合
            if (isNebulaScreenActive && !ButtonAdded)
            {
                // 既にボタンが追加されているかチェック
                Transform? existingButton = nebulaScreen!.transform.Find("MRIPSettingsButton");
                if (existingButton == null)
                {
                    NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("NebulaScreen became active, adding button..."));
                    AddButtonToNebulaScreen(nebulaScreen, mainMenu);
                    ButtonAdded = true;
                }
                else
                {
                    // 既存のボタンがある（以前追加された）
                    ButtonAdded = true;
                }
            }
            
            // NebulaScreenが閉じられたらリセット（次回開いた時に再追加できるように）
            if (wasNebulaScreenActive && !isNebulaScreenActive)
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("NebulaScreen closed, resetting button state."));
                ButtonAdded = false;
            }
            
            wasNebulaScreenActive = isNebulaScreenActive;
            
            // ロビーに入ったら終了（AmongUs.GameOptions.GameOptionsManagerが初期化されている）
            if (LobbyBehaviour.Instance != null)
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("Entered lobby, stopping monitor."));
                ButtonAdded = false;
                yield break;
            }
        }
        
        NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("Monitor timeout after 10 minutes."));
    }
    
    /// <summary>
    /// NebulaScreenにMRIPボタンを追加
    /// </summary>
    private static void AddButtonToNebulaScreen(GameObject nebulaScreen, MainMenuManager mainMenu)
    {
        try
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("Adding button to NebulaScreen..."));
            
            // 既存のボタンを探す
            PassiveButton? templateButton = null;
            int buttonCount = 0;
            
            for (int i = 0; i < nebulaScreen.transform.childCount; i++)
            {
                var child = nebulaScreen.transform.GetChild(i);
                var pb = child.GetComponent<PassiveButton>();
                if (pb != null && child.name.StartsWith("Account_CTA"))
                {
                    if (templateButton == null)
                    {
                        templateButton = pb;
                    }
                    buttonCount++;
                }
            }
            
            if (templateButton == null)
            {
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix("No template button found!"));
                return;
            }
            
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Found {buttonCount} buttons, using first as template"));
            
            // テンプレートボタンの位置情報
            UnityEngine.Vector3 templatePos = templateButton.transform.localPosition;
            
            // ボタン間隔を計算（2列構成）
            float spacingX = 0f;
            float spacingY = 0f;
            
            if (buttonCount >= 2)
            {
                // 2番目のボタンとの差でX間隔を計算
                for (int i = 0; i < nebulaScreen.transform.childCount; i++)
                {
                    var child = nebulaScreen.transform.GetChild(i);
                    if (child.GetComponent<PassiveButton>() != null && child.name.StartsWith("Account_CTA") && child.gameObject != templateButton.gameObject)
                    {
                        spacingX = child.localPosition.x - templatePos.x;
                        break;
                    }
                }
            }
            
            if (buttonCount >= 3)
            {
                // 3番目のボタンとの差でY間隔を計算
                int count = 0;
                for (int i = 0; i < nebulaScreen.transform.childCount; i++)
                {
                    var child = nebulaScreen.transform.GetChild(i);
                    if (child.GetComponent<PassiveButton>() != null && child.name.StartsWith("Account_CTA"))
                    {
                        count++;
                        if (count == 3)
                        {
                            spacingY = child.localPosition.y - templatePos.y;
                            break;
                        }
                    }
                }
            }
            
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Button spacing: X={spacingX}, Y={spacingY}"));
            
            // ボタンを複製
            GameObject mripButtonObj = UnityEngine.Object.Instantiate(templateButton.gameObject, nebulaScreen.transform);
            mripButtonObj.name = "MRIPSettingsButton";
            
            // 新しい位置を計算（最後の行の次の位置）
            int row = buttonCount / 2;
            int col = buttonCount % 2;
            float newX = templatePos.x + (spacingX * col);
            float newY = templatePos.y + (spacingY * row);
            
            mripButtonObj.transform.localPosition = new UnityEngine.Vector3(newX, newY, templatePos.z);
            
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Button position: ({newX}, {newY})"));
            
            // TextTranslatorTMPを無効化（翻訳で上書きされないように）
            var translators = mripButtonObj.GetComponentsInChildren<TextTranslatorTMP>(true);
            foreach (var translator in translators)
            {
                translator.enabled = false;
            }
            
            // テキストを変更
            var textMeshPros = mripButtonObj.GetComponentsInChildren<TextMeshPro>(true);
            foreach (var tmp in textMeshPros)
            {
                tmp.text = $"{MRIPInfo.ShortName}設定";
                NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Set button text: {tmp.text}"));
            }
            
            // クリックイベントを設定
            PassiveButton? mripButton = mripButtonObj.GetComponent<PassiveButton>();
            if (mripButton != null)
            {
                mripButton.OnClick = new Button.ButtonClickedEvent();
                mripButton.OnClick.AddListener((System.Action)delegate
                {
                    NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("MRIP button clicked!"));
                    VanillaAsset.PlaySelectSE();
                    mainMenu.ResetScreen();
                    
                    // MRIPVersionsScreenを作成または表示
                    if (MRIPVersionsScreen == null)
                    {
                        CreateVersionsScreen(mainMenu);
                    }
                    MRIPVersionsScreen?.SetActive(true);
                    mainMenu.screenTint.enabled = true;
                });
            }
            
            // アクティブにする
            mripButtonObj.SetActive(true);
            
            // デバッグ情報
            var sr = mripButtonObj.GetComponentInChildren<SpriteRenderer>(true);
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Button has SpriteRenderer: {sr != null}"));
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Button active: {mripButtonObj.activeSelf}, hierarchy: {mripButtonObj.activeInHierarchy}"));
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix($"Button layer: {mripButtonObj.layer}"));
            
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("MRIP button added to NebulaScreen!"));
        }
        catch (System.Exception ex)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix($"Error adding button: {ex.Message}\n{ex.StackTrace}"));
        }
    }
    
    /// <summary>
    /// バージョン選択画面を作成（Nebula風 - NebulaScreen内に配置）
    /// </summary>
    private static void CreateVersionsScreen(MainMenuManager mainMenu)
    {
        try
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("Creating MRIP versions screen..."));
            
            // accountButtonsの親と同じ階層に配置（Nebulaと同じ方式）
            MRIPVersionsScreen = UnityHelper.CreateObject("MRIPVersions", mainMenu.accountButtons.transform.parent, new UnityEngine.Vector3(0, 0, -1f));
            MRIPVersionsScreen.transform.localScale = MainMenuSetUpPatch.NebulaScreen!.transform.localScale;
            
            // MetaScreenを生成（GenerateWindowではなくGenerateScreen）
            var screen = MetaScreen.GenerateScreen(new UnityEngine.Vector2(6.2f, 4.1f), MRIPVersionsScreen.transform, new UnityEngine.Vector3(-0.1f, 0, 0f), false, false, false);
            
            // テキスト属性を設定
            TextAttributeOld NameAttribute = new TextAttributeOld(TextAttributeOld.BoldAttr)
            {
                FontMaterial = VanillaAsset.StandardMaskedFontMaterial,
                Size = new UnityEngine.Vector2(2.2f, 0.3f),
                Alignment = TMPro.TextAlignmentOptions.Left
            };
            
            TextAttributeOld CategoryAttribute = new TextAttributeOld(TextAttributeOld.BoldAttr)
            {
                FontMaterial = VanillaAsset.StandardMaskedFontMaterial,
                Size = new UnityEngine.Vector2(0.8f, 0.3f),
                Alignment = TMPro.TextAlignmentOptions.Center
            };
            CategoryAttribute.EditFontSize(1.2f, 0.6f, 1.2f);
            
            TextAttributeOld ButtonAttribute = new TextAttributeOld(TextAttributeOld.BoldAttr)
            {
                FontMaterial = VanillaAsset.StandardMaskedFontMaterial,
                Size = new UnityEngine.Vector2(1f, 0.2f),
                Alignment = TMPro.TextAlignmentOptions.Center
            };
            
            // 内部参照用変数
            Variable<MetaWidgetOld.ScrollView.InnerScreen> innerRef = new Variable<MetaWidgetOld.ScrollView.InnerScreen>();
            List<MRIPModUpdater.ReleasedInfo>? versions = MRIPModUpdater.Cache;
            
            // 静的ウィジェット（全体レイアウト）
            MetaWidgetOld staticWidget = new MetaWidgetOld();
            
            // 左側: カテゴリボタン（Nebulaと同じ方式）
            MetaWidgetOld menuWidget = new MetaWidgetOld();
            
            // 各カテゴリのボタンを追加
            foreach (MRIPModUpdater.ReleaseCategory category in System.Enum.GetValues(typeof(MRIPModUpdater.ReleaseCategory)))
            {
                var cat = category; // クロージャ用
                menuWidget.Append(new MetaWidgetOld.Button(() => UpdateContents(cat), 
                    new TextAttributeOld(TextAttributeOld.BoldAttr) { Size = new UnityEngine.Vector2(0.95f, 0.28f) }) 
                { 
                    RawText = MRIPModUpdater.CategoryNames[(int)category] 
                });
            }
            
            // 左側メニュー + 右側スクロールビュー（ParallelWidgetOld）
            staticWidget.Append(new ParallelWidgetOld(
                new System.Tuple<IMetaWidgetOld, float>(new MetaWidgetOld.HorizonalMargin(0.1f), 0.1f),
                new System.Tuple<IMetaWidgetOld, float>(menuWidget, 1f),
                new System.Tuple<IMetaWidgetOld, float>(new MetaWidgetOld.HorizonalMargin(0.1f), 0.1f),
                new System.Tuple<IMetaWidgetOld, float>(new MetaWidgetOld.ScrollView(new UnityEngine.Vector2(5f, 4f), new MetaWidgetOld(), true) 
                { 
                    Alignment = IMetaWidgetOld.AlignmentOption.Center, 
                    InnerRef = innerRef,
                    ScrollerTag = "MRIPVersions"
                }, 5f)
            ));
            
            screen.SetWidget(staticWidget);
            
            // ローディング表示
            innerRef.Value?.SetLoadingWidget();
            
            /// <summary>
            /// コンテンツを更新
            /// </summary>
            void UpdateContents(MRIPModUpdater.ReleaseCategory? category = null)
            {
                if (versions == null || versions.Count == 0)
                {
                    innerRef.Value?.SetWidget(new MetaWidgetOld.Text(NameAttribute) { RawText = "バージョン情報を読み込み中..." });
                    return;
                }
                
                var inner = new MetaWidgetOld();
                
                // 自動更新設定行（現在は使用していない）
                /*
                void AutoUpdateContent(string label, MRIPAutoUpdater.AutoUpdateMode mode)
                {
                    var currentMode = MRIPAutoUpdater.GetAutoUpdateMode();
                    List<IMetaParallelPlacableOld> placeable = new List<IMetaParallelPlacableOld>();
                    
                    // カテゴリラベル
                    placeable.Add(new MetaWidgetOld.Text(CategoryAttribute) { RawText = "自動更新" });
                    placeable.Add(new MetaWidgetOld.HorizonalMargin(0.15f));
                    
                    // バージョン名
                    placeable.Add(new MetaWidgetOld.Text(NameAttribute) { RawText = label });
                    placeable.Add(new MetaWidgetOld.HorizonalMargin(0.15f));
                    
                    // ボタンまたは適用中表示
                    if (currentMode != mode)
                    {
                        placeable.Add(new MetaWidgetOld.Button(() => 
                        {
                            MRIPAutoUpdater.SetAutoUpdateMode(mode);
                            UpdateContents(category);
                            MetaUI.ShowConfirmDialog(null, new RawTextComponent("自動更新モードを設定しました"));
                        }, ButtonAttribute) 
                        { 
                            RawText = "適用",
                            PostBuilder = (_, renderer, _) => renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask
                        });
                    }
                    else
                    {
                        placeable.Add(new MetaWidgetOld.HorizonalMargin(0.13f));
                        placeable.Add(new MetaWidgetOld.Text(ButtonAttribute) { RawText = "適用中" });
                    }
                    
                    inner.Append(new CombinedWidgetOld(0.5f, placeable.ToArray()) { Alignment = IMetaWidgetOld.AlignmentOption.Left });
                }
                */
                
                // 自動更新ボタンは無効化
                // // 安定版カテゴリの場合に自動更新（安定版）行を表示
                // if ((category ?? MRIPModUpdater.ReleaseCategory.Major) == MRIPModUpdater.ReleaseCategory.Major)
                // {
                //     AutoUpdateContent("最新の安定版", MRIPAutoUpdater.AutoUpdateMode.Major);
                // }
                // 
                // // スナップショットカテゴリの場合に自動更新（スナップショット）行を表示
                // if ((category ?? MRIPModUpdater.ReleaseCategory.Snapshot) == MRIPModUpdater.ReleaseCategory.Snapshot)
                // {
                //     AutoUpdateContent("最新のスナップショット", MRIPAutoUpdater.AutoUpdateMode.Snapshot);
                // }
                
                // バージョン一覧
                foreach (var version in versions)
                {
                    // カテゴリフィルタ
                    if (category != null && version.Category != category) continue;
                    
                    try
                    {
                        List<IMetaParallelPlacableOld> placeable = new List<IMetaParallelPlacableOld>();
                        
                        // カテゴリラベル（色付き）
                        placeable.Add(new MetaWidgetOld.Text(CategoryAttribute) 
                        { 
                            MyText = NebulaGUIWidgetEngine.Instance.TextComponent(
                                MRIPModUpdater.CategoryColors[(int)version.Category], 
                                MRIPModUpdater.CategoryNames[(int)version.Category])
                        });
                        placeable.Add(new MetaWidgetOld.HorizonalMargin(0.15f));
                        
                        // バージョン名（クリックでリリースページを開く、ホバーで説明表示）
                        placeable.Add(new MetaWidgetOld.Text(NameAttribute)
                        {
                            RawText = version.DisplayVersion,
                            PostBuilder = text =>
                            {
                                var button = text.gameObject.SetUpButton(true);
                                button.gameObject.AddComponent<BoxCollider2D>().size = text.rectTransform.sizeDelta;
                                button.OnClick.AddListener(() => Application.OpenURL($"https://github.com/10-ui/MoreRolesInPolus/releases/tag/{version.RawTag}"));
                                button.OnMouseOver.AddListener(() =>
                                {
                                    text.color = UnityEngine.Color.green;
                                    if (version.Body != null) NebulaManager.Instance.SetHelpWidget(button, version.Body);
                                });
                                button.OnMouseOut.AddListener(() =>
                                {
                                    text.color = UnityEngine.Color.white;
                                    NebulaManager.Instance.HideHelpWidgetIf(button);
                                });
                            }
                        });
                        placeable.Add(new MetaWidgetOld.HorizonalMargin(0.15f));
                        
                        // ボタン: 取得/使用中
                        if (version.IsCurrentVersion())
                        {
                            // 現在のバージョン
                            placeable.Add(new MetaWidgetOld.HorizonalMargin(0.13f));
                            placeable.Add(new MetaWidgetOld.Text(ButtonAttribute) { RawText = "使用中" });
                        }
                        else if (!string.IsNullOrEmpty(version.DownloadUrl))
                        {
                            // ダウンロード可能
                            placeable.Add(new MetaWidgetOld.Button(() => 
                            {
                                NebulaManager.Instance.StartCoroutine(version.CoUpdateAndShowDialog().WrapToIl2Cpp());
                            }, ButtonAttribute) 
                            { 
                                RawText = "取得",
                                PostBuilder = (_, renderer, _) => renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask
                            });
                        }
                        else
                        {
                            // ダウンロードURL不明
                            placeable.Add(new MetaWidgetOld.HorizonalMargin(0.13f));
                            placeable.Add(new MetaWidgetOld.Text(ButtonAttribute) { RawText = "---" });
                        }
                        
                        inner.Append(new CombinedWidgetOld(0.5f, placeable.ToArray()) { Alignment = IMetaWidgetOld.AlignmentOption.Left });
                    }
                    catch (System.Exception ex)
                    {
                        NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix($"Error displaying version {version.RawTag}: {ex.Message}"));
                    }
                }
                
                // もっと読み込むボタン
                if (!MRIPModUpdater.MaybeNoMorePages)
                {
                    inner.Append(new MetaWidgetOld.Button(() =>
                    {
                        NebulaManager.Instance.StartCoroutine(MRIPModUpdater.CoFetchVersionTags((list) =>
                        {
                            versions = list;
                            UpdateContents(category);
                        }).WrapToIl2Cpp());
                    }, ButtonAttribute)
                    { 
                        Alignment = IMetaWidgetOld.AlignmentOption.Center, 
                        RawText = "もっと読み込む",
                        PostBuilder = (_, renderer, _) => renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask
                    });
                }
                
                innerRef.Value?.SetWidget(inner);
            }
            
            // 初期データ読み込み
            if (MRIPModUpdater.Cache != null && MRIPModUpdater.Cache.Count > 0)
            {
                versions = MRIPModUpdater.Cache;
                UpdateContents();
            }
            else
            {
                NebulaManager.Instance.StartCoroutine(MRIPModUpdater.CoFetchVersionTags((list) => 
                {
                    versions = list;
                    
                    // エラーチェック: データが取得できなかった場合
                    if (list == null || list.Count == 0)
                    {
                        var errorWidget = new MetaWidgetOld();
                        errorWidget.Append(new MetaWidgetOld.Text(NameAttribute) 
                        { 
                            RawText = "エラー: バージョン情報を取得できませんでした",
                            Alignment = IMetaWidgetOld.AlignmentOption.Center
                        });
                        errorWidget.Append(new MetaWidgetOld.VerticalMargin(0.3f));
                        errorWidget.Append(new MetaWidgetOld.Text(CategoryAttribute) 
                        { 
                            RawText = "GitHub APIへのアクセスが制限されています。\n\nしばらく待ってから再度お試しください。",
                            Alignment = IMetaWidgetOld.AlignmentOption.Center
                        });
                        innerRef.Value?.SetWidget(errorWidget);
                    }
                    else
                    {
                        UpdateContents();
                    }
                }).WrapToIl2Cpp());
            }
            
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Log, MRIPInfo.LogPrefix("MRIP versions screen created!"));
        }
        catch (System.Exception ex)
        {
            NebulaPlugin.Log.Print(NebulaLog.LogLevel.Error, MRIPInfo.LogPrefix($"Error creating versions screen: {ex.Message}\n{ex.StackTrace}"));
        }
    }
}
