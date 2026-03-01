/**
 * Coordinator.cs
 * 
 * 概要: Coordinator役職（第三陣営版）の情報を定義するクラスです。
 * 仕様:
 *   - プレイヤーを選択し、部屋を推測してスコアを獲得する能力を持つ
 *   - 累積スコアが設定値に達すると単独勝利
 *   - キル能力は持たない
 *   - ベント使用可能、インポスター視界、停電無効
 * 制限:
 *   - 推測が外れた場合はターゲットに通知される
 */
using Nebula.Modules;
using Nebula.Utilities;
using System.Linq;
using MoreRolesInPolus.Helpers;
using Nebula.Player;
using Virial.Game;
using Il2CppInterop.Runtime;
using Virial.Events.Game.Minimap;
using Virial.Runtime;
using Virial.Events.Player;

// 型の曖昧さを解消
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;

namespace MoreRolesInPolus.Roles.Neutral;

/// <summary>
/// Coordinatorのチーム情報を定義するクラス（Accuserパターンに準拠）
/// </summary>
[NebulaPreprocess(PreprocessPhase.BuildAssignmentTypes)]
internal class CoordinatorTeamInfo
{
    static public RoleTeam? MyTeam { get; private set; }
    static public GameEnd? End { get; private set; }
    static public Virial.Color TeamColor { get; private set; }
    
    static private void Preprocess(NebulaPreprocessor preprocessor)
    {
        TeamColor = new(229, 151, 150);
        MyTeam = preprocessor.CreateTeam("teams.coordinator", TeamColor, TeamRevealType.OnlyMe);
        End = preprocessor.CreateEnd("coordinator", TeamColor);
    }
}

/// <summary>
/// Coordinatorロールの情報を定義するクラスです。
/// プレイヤーを選択し、部屋を推測してスコアを獲得する能力を持ちます。
/// 累積スコアが設定値に達すると単独勝利します。
/// </summary>
internal class Coordinator : DefinedRoleTemplate, DefinedRole
{
    /// <summary>
    /// 座標特定クールダウン（最小値）5000pt時に適用
    /// </summary>
    static private readonly FloatConfiguration CooldownMinOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.cooldownMin", (2.5f, 30f, 2.5f), 10f, FloatConfigurationDecorator.Second);
    
    /// <summary>
    /// 座標特定クールダウン（最大値）0pt時に適用
    /// </summary>
    static private readonly FloatConfiguration CooldownMaxOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.cooldownMax", (10f, 60f, 5f), 40f, FloatConfigurationDecorator.Second);
    
    /// <summary>
    /// 初期クールダウンを上書きするか
    /// </summary>
    static private readonly BoolConfiguration OverrideInitialCooldownOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.overrideInitialCooldown", false);
    
    /// <summary>
    /// 初期クールダウン（上書き有効時）
    /// </summary>
    static private readonly FloatConfiguration InitialCooldownOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.initialCooldown", (5f, 60f, 2.5f), 15f, FloatConfigurationDecorator.Second, () => OverrideInitialCooldownOption);
    
    /// <summary>
    /// 勝利に必要なポイント倍率（5000ベース）
    /// x1=5000, x2=10000, x4=20000, x6=30000
    /// </summary>
    static private readonly IntegerConfiguration PointsMultiplierOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.pointsMultiplier", (1, 8), 4);

    /// <summary>
    /// 勝利に必要なポイント数を計算
    /// </summary>
    public static int PointsToWin => PointsMultiplierOption * 5000;

    /// <summary>
    /// 役職の情報を用意します。
    /// </summary>
    static public Coordinator MyRole = new();

    /// <summary>
    /// 統計：座標特定回数
    /// </summary>
    static private GameStatsEntry StatsCoordinate = NebulaAPI.CreateStatsEntry("stats.coordinator.coordinate", GameStatsCategory.Roles, MyRole);

    /// <summary>
    /// Coordinatorロール情報のコンストラクタです。
    /// </summary>
    private Coordinator() : base("coordinator", CoordinatorTeamInfo.TeamColor, RoleCategory.NeutralRole, CoordinatorTeamInfo.MyTeam, [CooldownMinOption, CooldownMaxOption, OverrideInitialCooldownOption, InitialCooldownOption, PointsMultiplierOption])
    {
        ConfigurationHolder?.AddTags(ConfigurationTags.TagFunny);
    }

    Image? DefinedAssignable.IconImage => iconImage;
    static readonly Image iconImage = NebulaAPI.AddonAsset.GetResource("Neutral/Coordinator/Coordinator.png")!.AsImage()!;

    /// <summary>
    /// ランタイムロールを生成
    /// </summary>
    RuntimeRole RuntimeAssignableGenerator<RuntimeRole>.CreateInstance(GamePlayer player, int[] arguments) => new Instance(player, arguments);

    /// <summary>
    /// 役職の能力を記述するクラスです。
    /// </summary>
    public class Instance : RuntimeAssignableTemplate, RuntimeRole, ICoordinatorMapCallback
    {
        DefinedRole RuntimeRole.Role => MyRole;

        /// <summary>
        /// ベント使用可能
        /// </summary>
        bool RuntimeRole.CanUseVent => true;

        /// <summary>
        /// インポスター視界
        /// </summary>
        bool RuntimeRole.HasImpostorVision => true;

        /// <summary>
        /// 停電無効
        /// </summary>
        bool RuntimeRole.IgnoreBlackout => true;
        
        /// <summary>
        /// 選択されたターゲットプレイヤー
        /// </summary>
        private GamePlayer? SelectedTarget = null;

        /// <summary>
        /// 部屋選択画面のMetaScreen参照
        /// </summary>
        private MetaScreen? RoomSelectScreen = null;

        /// <summary>
        /// アビリティボタンの参照
        /// </summary>
        private ModAbilityButton? CoordinateButton = null;

        /// <summary>
        /// 累積スコア
        /// </summary>
        private int TotalScore = 0;

        static private readonly Image CoordinateSprite = NebulaAPI.AddonAsset.GetResource("Neutral/Coordinator/CoordinateButton.png")!.AsImage(115f)!;


        /// <summary>
        /// 役職の引数を取得（スコアの引き継ぎ用）
        /// </summary>
        int[]? RuntimeAssignable.RoleArguments => [TotalScore];

        /// <summary>
        /// 役職能力のコンストラクタ。
        /// </summary>
        /// <param name="player">割り当て対象のプレイヤー</param>
        /// <param name="arguments">引数（スコア引き継ぎ用）</param>
        public Instance(GamePlayer player, int[] arguments) : base(player)
        {
            TotalScore = arguments.Length >= 1 ? arguments[0] : 0;
        }

        /// <summary>
        /// 役職がアクティブ化された時の処理
        /// </summary>
        void RuntimeAssignable.OnActivated()
        {
            if (AmOwner)
            {
                // 初期クールダウンを計算（上書き有効時は設定値、無効時は中間値）
                float minCooldown = CooldownMinOption;
                float maxCooldown = CooldownMaxOption;
                float initialCooldown;
                
                if (OverrideInitialCooldownOption)
                {
                    initialCooldown = InitialCooldownOption;
                }
                else
                {
                    initialCooldown = (minCooldown + maxCooldown) / 2f;
                }
                
                CoordinateButton = NebulaAPI.Modules.AbilityButton(this, MyPlayer, Virial.Compat.VirtualKeyInput.Ability,
                    maxCooldown, "coordinate", CoordinateSprite, 
                    null, _ => true);

                // ボタンをModAbilityButtonImplにキャストして設定
                if (CoordinateButton is Nebula.Modules.ScriptComponents.ModAbilityButtonImpl buttonImpl)
                {
                    // 自動クールダウンリセットを無効化
                    buttonImpl.UseCoolDownSupport = false;
                    
                    // カスタムタイマーを作成
                    var timer = new Nebula.Modules.ScriptComponents.AdvancedTimer(initialCooldown, maxCooldown)
                        .SetDefault(maxCooldown)
                        .SetAsAbilityCoolDown()
                        .Start(new float?(initialCooldown))
                        .Register(this, null);
                    buttonImpl.CoolDownTimer = timer;
                }

                // ボタンクリック時の処理
                CoordinateButton.OnClick = (button) =>
                {
                    // プレイヤー選択画面を開く
                    OpenPlayerSelectScreen();
                };

                CoordinateButton.SetLabel("coordinate");
            }
        }

        /// <summary>
        /// タスクパネルにスコアを追加
        /// </summary>
        [Local]
        void AppendScoreToTaskPanel(PlayerTaskTextLocalEvent ev)
        {
            ev.AppendText(Language.Translate("role.coordinator.taskText").Replace("%SCORE%", TotalScore.ToString()).Replace("%GOAL%", PointsToWin.ToString()).Color(CoordinatorTeamInfo.MyTeam!.UnityColor));
        }

        /// <summary>
        /// 現在のミニゲームインスタンス
        /// </summary>
        private Minigame? PlayerSelectMinigame = null;

        /// <summary>
        /// プレイヤー選択画面を開く処理
        /// </summary>
        void OpenPlayerSelectScreen()
        {
            // 生存している他のプレイヤーのリストを取得（自分以外）
            var alivePlayers = GamePlayer.AllPlayers.Where(p => !p.IsDead && !p.AmOwner).ToList();
            
            // プレイヤー名でソート
            alivePlayers.Sort((p1, p2) => p1.Name.CompareTo(p2.Name));

            if (alivePlayers.Count == 0) return;

            // ShapeshifterMinigameのプレハブを検索
            ShapeshifterMinigame? prefab = null;
            var foundObjects = UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppType.Of<ShapeshifterMinigame>());
            foreach (var obj in foundObjects)
            {
                var minigamePrefab = obj.TryCast<ShapeshifterMinigame>();
                if (minigamePrefab != null && minigamePrefab.gameObject.scene.name == null)
                {
                    prefab = minigamePrefab;
                    break;
                }
            }
            if (prefab == null) return;

            // ミニゲーム生成
            var minigame = Object.Instantiate(prefab).Cast<ShapeshifterMinigame>();
            minigame.transform.SetParent(UnityEngine.Camera.main.transform, false);
            minigame.transform.localPosition = new Vector3(0f, 0f, -50f);

            // パネル配置用リスト
            Il2CppSystem.Collections.Generic.List<UiElement> controllerButtons = new Il2CppSystem.Collections.Generic.List<UiElement>();

            // パネル生成ループ
            float xStart = minigame.XStart;
            float yStart = minigame.YStart;
            float xOffset = minigame.XOffset;
            float yOffset = minigame.YOffset;

            for (int i = 0; i < alivePlayers.Count; i++)
            {
                var targetPlayer = alivePlayers[i];
                int col = i % 3;
                int row = i / 3;
                
                // パネル生成
                ShapeshifterPanel panel = Object.Instantiate(minigame.PanelPrefab, minigame.transform).Cast<ShapeshifterPanel>();
                panel.transform.localPosition = new Vector3(xStart + (float)col * xOffset, yStart + (float)row * yOffset, -1f);
                
                // プレイヤー情報セット
                panel.SetPlayer(targetPlayer.PlayerId, targetPlayer.VanillaPlayer.Data, new System.Action(() => 
                {
                    SetPlayer(targetPlayer.PlayerId);
                }));

                // 名前の色設定（すべて白、インポスターも区別しない）
                panel.NameText.color = Palette.White;

                // カーソル表示 (選択中のプレイヤーなら表示)
                if (SelectedTarget != null && SelectedTarget.PlayerId == targetPlayer.PlayerId)
                {
                    var cursor = new UnityEngine.GameObject("Cursor");
                    cursor.transform.SetParent(minigame.transform);
                    cursor.transform.localPosition = new Vector3(0, 0, -5f);
                    var cursorSprite = cursor.AddComponent<UnityEngine.SpriteRenderer>();
                    
                    if (MapBehaviour.Instance && MapBehaviour.Instance.HerePoint) 
                    {
                        cursorSprite.sprite = MapBehaviour.Instance.HerePoint.sprite;
                        cursorSprite.color = CoordinatorTeamInfo.MyTeam!.UnityColor;
                    }
                }

                controllerButtons.Add(panel.Button);
            }

            // 閉じるボタンの設定
            if (minigame.BackButton != null)
            {
                var backButton = minigame.BackButton.TryCast<PassiveButton>();
                if (backButton != null)
                {
                    backButton.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => ClosePlayerSelectMinigame()));
                }
            }

            // コントローラー/オーバーレイ表示
            ControllerManager.Instance.OpenOverlayMenu(minigame.name, minigame.BackButton, minigame.DefaultButtonSelected, controllerButtons, false);
            
            PlayerSelectMinigame = minigame;
        }

        /// <summary>
        /// プレイヤー選択ミニゲームを閉じる
        /// </summary>
        void ClosePlayerSelectMinigame()
        {
            if (PlayerSelectMinigame != null)
            {
                ControllerManager.Instance.CloseOverlayMenu(PlayerSelectMinigame.name);
                Object.Destroy(PlayerSelectMinigame.gameObject);
                PlayerSelectMinigame = null;
            }
        }

        // マップレイヤーのインスタンス
        private CoordinatorMapLayer? mapLayer = null;

        // プレイヤーが選択された時の処理
        private void SetPlayer(byte playerId)
        {
            // PlayerControlを取得し、GamePlayer (Virial.Game.Player) に変換する必要がある
            var target = GamePlayer.AllPlayers.FirstOrDefault(p => p.PlayerId == playerId);
            if (target == null) return;

            SelectedTarget = target;
            
            // プレイヤー選択画面を閉じる
            ClosePlayerSelectMinigame();

            // マップを開く (HudManager経由)
            HudManager.Instance.ToggleMapVisible(new MapOptions { Mode = MapOptions.Modes.Normal, AllowMovementWhileMapOpen = true });
        }

        // マップが開かれた時のイベント
        [Local]
        void OnOpenMap(AbstractMapOpenEvent ev)
        {
            
            // ターゲットが選択されていない、または死んでいる場合は何もしない
            if (SelectedTarget == null || MyPlayer.IsDead) 
            {
                if (mapLayer != null) mapLayer.gameObject.SetActive(false);
                return;
            }

            // 通常マップイベントのみ対応
            if (ev is MapOpenNormalEvent && !MeetingHud.Instance)
            {
                if (mapLayer == null)
                {
                    // レイヤー未生成なら作成
                    mapLayer = UnityHelper.CreateObject<CoordinatorMapLayer>("CoordinatorLayer", MapBehaviour.Instance.transform, new Vector3(0f, 0f, -1f), null);
                    mapLayer.InjectCallback(this);
                }

                // 表示
                mapLayer.gameObject.SetActive(true);
            }
            else
            {
                // 条件を満たさない場合は非表示
                if (mapLayer != null) mapLayer.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// ICoordinatorMapCallback実装: 部屋選択時のコールバック
        /// </summary>
        /// <param name="room">選択された部屋</param>
        /// <param name="clickedWorldPos">クリックしたワールド座標</param>
        public void OnRoomSelected(SystemTypes room, Vector2 clickedWorldPos)
        {
            ExecuteCoordinate(SelectedTarget!, room, clickedWorldPos);
        }

        /// <summary>
        /// SystemTypesから部屋名を取得するヘルパー
        /// </summary>
        string GetRoomNameFromSystemType(SystemTypes roomType)
        {
            PlainShipRoom room;
            if (ShipStatus.Instance.FastRooms.TryGetValue(roomType, out room) && room.roomArea != null)
            {
                var center = room.roomArea.bounds.center;
                return AmongUsUtil.GetRoomName(new Vector2(center.x, center.y), false, false);
            }
            return roomType.ToString();
        }

        /// <summary>
        /// 座標判定を実行してスコアを加算する処理
        /// </summary>
        /// <param name="target">ターゲットプレイヤー</param>
        /// <param name="targetRoom">推測した部屋タイプ</param>
        /// <param name="clickedWorldPos">クリックしたワールド座標</param>
        void ExecuteCoordinate(GamePlayer target, SystemTypes targetRoom, Vector2 clickedWorldPos)
        {
            if (target.IsDead)
            {
                var titleShower = NebulaAPI.CurrentGame!.GetModule<TitleShower>();
                var infoColor = new Color(0.1f, 0.2f, 0.8f);
                if (titleShower != null)
                {
                    titleShower.SetText($"{target.Name} は既に死んでいるようだ...", infoColor, 3f, true);
                }
                AmongUsUtil.PlayCustomFlash(infoColor, 0f, 0.5f, 0.4f, 0f);
                CloseAllScreens();
                return;
            }

            // 統計：座標特定回数を記録
            StatsCoordinate.Progress();

            // 精度距離: クリック位置とターゲット位置の距離（小さいほど良い）
            Vector2 targetPos = target.TruePosition;
            float accuracyDistance = Vector2.Distance(clickedWorldPos, targetPos);
            
            // 射程距離: 自分からターゲットまでの距離（大きいほど良い）
            Vector2 myPos = MyPlayer.TruePosition;
            float rangeDistance = Vector2.Distance(myPos, targetPos);
            

            

            // ターゲットの現在部屋を判定
            bool isCorrect = false;
            
            if (ShipStatus.Instance.FastRooms.TryGetValue(targetRoom, out var room) && room.roomArea != null)
            {
                if (room.roomArea.OverlapPoint(target.TruePosition))
                {
                    isCorrect = true;
                }
            }

            // スコア計算
            // 距離スコア: 最大3000pt（遠いほど高い）
            // マップの端から端は約40m程度を想定
            const float maxRangeDistance = 40f;  // これ以上で距離スコア最大
            const int maxRangeScore = 3000;
            float rangeRatio = UnityEngine.Mathf.Clamp01(rangeDistance / maxRangeDistance);
            int rangeScore = (int)(rangeRatio * maxRangeScore);

            // 精度スコア: 最大2000pt（正確なほど高い）
            const float maxAccuracyDistance = 10f;  // これ以上で精度スコア0
            const int maxAccuracyScore = 2000;
            float accuracyRatio = UnityEngine.Mathf.Clamp01(1f - accuracyDistance / maxAccuracyDistance);
            int accuracyScore = (int)(accuracyRatio * maxAccuracyScore);

            // 合計スコア（最大5000pt）
            int score = rangeScore + accuracyScore;

            // 同じ部屋ペナルティ: 自分とターゲットが同じ部屋にいる場合 -1000pt
            bool sameRoom = false;
            if (ShipStatus.Instance.FastRooms.TryGetValue(targetRoom, out var myRoom) && myRoom.roomArea != null)
            {
                if (myRoom.roomArea.OverlapPoint(myPos))
                {
                    sameRoom = true;
                    score -= 1000;
                }
            }
            
            // クールダウン計算（スコアに応じて線形補間）
            // 5000pt = 最小クールダウン, 0pt = 最大クールダウン, 2500pt = ちょうど中間
            float minCooldown = CooldownMinOption;
            float maxCooldown = CooldownMaxOption;
            float scoreRatio = score / 5000f;  // 0.0 ~ 1.0
            
            // 高スコア(1.0) = minCooldown, 低スコア(0.0) = maxCooldown
            float cooldown = UnityEngine.Mathf.Lerp(maxCooldown, minCooldown, scoreRatio);
            

            
            if (isCorrect)
            {
                // 正解：スコアを加算
                TotalScore += score;
                // スコア表示（緑色）+ フラッシュ
                var currentGame = NebulaAPI.CurrentGame;
                if (currentGame != null)
                {
                    var titleShower = currentGame.GetModule<Nebula.Game.TitleShower>();
                    if (titleShower != null)
                    {
                        titleShower.SetText($"+{score} POINT! (Total: {TotalScore}/{PointsToWin})", new Color(0.2f, 1f, 0.4f), 3f, true);
                    }
                }
                Nebula.Utilities.AmongUsUtil.PlayCustomFlash(new Color(0.2f, 1f, 0.4f), 0f, 0.5f, 0.4f, 0f);

                // 勝利判定
                if (TotalScore >= PointsToWin)
                {
                    var bitmask = BitMasks.AsPlayer();
                    bitmask.Add(MyPlayer);
                    NebulaAPI.CurrentGame?.RequestGameEnd(CoordinatorTeamInfo.End, bitmask);
                }
            }
            else
            {
                // スコア表示（赤色）+ フラッシュ
                var currentGame = NebulaAPI.CurrentGame;
                if (currentGame != null)
                {
                    var titleShower = currentGame.GetModule<Nebula.Game.TitleShower>();
                    if (titleShower != null)
                    {
                        titleShower.SetText($"{score} POINT... (Total: {TotalScore}/{PointsToWin})", new Color(1f, 0.3f, 0.3f), 3f, true);
                    }
                }
                Nebula.Utilities.AmongUsUtil.PlayCustomFlash(new Color(1f, 0.3f, 0.3f), 0f, 0.5f, 0.4f, 0f);
                
                // 不正解：ターゲットに通知（通知がペナルティ）
                CoordinatorHelpers.RpcHolder.RpcNotifyTarget.Invoke(target.PlayerId);
            }

            // クールダウンを設定
            NebulaManager.Instance.ScheduleDelayAction(() =>
            {
                if (CoordinateButton?.CoolDownTimer is Nebula.Modules.ScriptComponents.AdvancedTimer timer)
                {
                    // ビジュアル用の最大値も更新してからスタート
                    timer.SetDefault(cooldown);
                    timer.Start(new float?(cooldown));
                }
            });

            // 画面を閉じる
            CloseAllScreens();
        }

        /// <summary>
        /// プレイヤーが死亡した時に画面を閉じる
        /// </summary>
        [Local, OnlyMyPlayer]
        void OnDead(PlayerDieEvent ev)
        {
            CloseAllScreens();
        }

        /// <summary>
        /// 前回通知した閾値レベル（0=未通知、1=50%、2=75%、3=90%）
        /// </summary>
        private int LastNotifiedThreshold = 0;

        /// <summary>
        /// 会議開始時に画面を閉じ、閾値を超えた場合スコアを全員に共有
        /// </summary>
        [Local]
        void OnMeetingStart(MeetingStartEvent ev)
        {
            CloseAllScreens();

            if (!AmOwner) return;

            // 現在の進捗率を計算
            float progress = (float)TotalScore / PointsToWin;
            
            // 閾値チェック: 50%, 75%, 90%
            int currentThreshold = 0;
            if (progress >= 0.90f) currentThreshold = 3;
            else if (progress >= 0.75f) currentThreshold = 2;
            else if (progress >= 0.50f) currentThreshold = 1;

            // 新しい閾値を超えた場合のみ通知
            if (currentThreshold > LastNotifiedThreshold)
            {
                LastNotifiedThreshold = currentThreshold;
                CoordinatorHelpers.RpcHolder.RpcShareScore.Invoke((MyPlayer.PlayerId, TotalScore, PointsToWin));
            }
        }

        /// <summary>
        /// 会議終了後（タスクフェーズ再開時）にクールダウンをリセット
        /// </summary>
        [Local]
        void OnTaskPhaseRestart(TaskPhaseRestartEvent ev)
        {
            if (CoordinateButton?.CoolDownTimer is Nebula.Modules.ScriptComponents.AdvancedTimer timer)
            {
                // 会議後は中間値のクールダウンでリセット
                float minCooldown = CooldownMinOption;
                float maxCooldown = CooldownMaxOption;
                float cooldown = (minCooldown + maxCooldown) / 2f;
                timer.Start(new float?(cooldown));
            }
        }

        /// <summary>
        /// すべての画面を閉じる
        /// </summary>
        private void CloseAllScreens()
        {
            ClosePlayerSelectMinigame();

            // マップを閉じる（HudManager経由）
            if (MapBehaviour.Instance && MapBehaviour.Instance.IsOpen)
            {
                HudManager.Instance.ToggleMapVisible(new MapOptions { Mode = MapOptions.Modes.Normal });
            }

            // レイヤー非表示
            if (mapLayer != null)
            {
                mapLayer.gameObject.SetActive(false);
            }
            
            // ターゲット選択解除
            SelectedTarget = null;

            if (RoomSelectScreen != null)
            {
                RoomSelectScreen.CloseScreen();
                RoomSelectScreen = null;
            }
        }
    }
}
