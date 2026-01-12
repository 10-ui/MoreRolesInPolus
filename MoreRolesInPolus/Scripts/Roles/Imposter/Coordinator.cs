
namespace MoreRolesInPolus.Roles.Imposter;

using Nebula.Modules;
using Nebula.Utilities;
using UnityEngine;
using System.Linq;
using MoreRolesInPolus.Helpers;
using Nebula.Player;
using Virial.Game;
using Il2CppInterop.Runtime;
using Virial.Events.Game.Minimap;

/// <summary>
/// Coordinatorロールの情報を定義するクラスです。
/// プレイヤーを選択し、部屋を推測して当たればキルできる能力を持ちます。
/// </summary>
public class Coordinator : DefinedSingleAbilityRoleTemplate<Coordinator.Ability>, DefinedRole
{
    /// <summary>
    /// Coordinatorロール情報のコンストラクタです。ここで役職の内部名、色、割り当てのカテゴリ、所属陣営、およびオプションを設定します。
    /// </summary>
    private Coordinator() : base("coordinator", new(Palette.ImpostorRed), RoleCategory.ImpostorRole, NebulaTeams.ImpostorTeam, [CoordinateCoolDownOption, KillCooldownMinOption, KillCooldownMaxOption])
    {
        ConfigurationHolder?.AddTags(ConfigurationTags.TagFunny);
    }

    /// <summary>
    /// ロビーで変更できる設定を用意します。ゲーム中で編集できるように、すぐ上のコンストラクタで役職のオプションに追加します。
    /// </summary>
    static private readonly FloatConfiguration CoordinateCoolDownOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.coordinateCoolDown", (0f, 60f, 2.5f), 25f, FloatConfigurationDecorator.Second);
    
    /// キルクールダウン（最小値）
    static private readonly FloatConfiguration KillCooldownMinOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.killCooldownMin", (0f, 30f, 2.5f), 2.5f, FloatConfigurationDecorator.Second);
    
    /// キルクールダウン（最大値）
    static private readonly FloatConfiguration KillCooldownMaxOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.killCooldownMax", (30f, 120f, 5f), 60f, FloatConfigurationDecorator.Second);

    /// <summary>
    /// 役職の情報を用意します。
    /// </summary>
    static public readonly Coordinator MyRole = new();
    AbilityAssignmentStatus DefinedRole.AssignmentStatus => AbilityAssignmentStatus.KillersSide;
    MultipleAssignmentType DefinedRole.MultipleAssignment => MultipleAssignmentType.Allowed;

    /// <summary>
    /// 役職を割り当てるとき、プレイヤーに割り当てる能力を作成します。
    /// </summary>
    /// <param name="player">割り当てる対象のプレイヤー</param>
    /// <param name="arguments">役職の引数(役職の状態を引き継ぐために使用します。)</param>
    /// <returns></returns>
    public override Ability CreateAbility(GamePlayer player, int[] arguments) => new(player, arguments.GetAsBool(0));

    /// <summary>
    /// 役職の能力を記述するクラスです。
    /// </summary>
    public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility, ICoordinatorMapCallback
    {
        /// <summary>
        /// 通常キルボタンを非表示（座標特定でのみキル可能）
        /// </summary>
        bool IPlayerAbility.HideKillButton => true;
        
        /// <summary>
        /// 選択されたターゲットプレイヤー
        /// </summary>
        private GamePlayer SelectedTarget = null;

        /// <summary>
        /// 部屋選択画面のMetaScreen参照
        /// </summary>
        private MetaScreen? RoomSelectScreen = null;

        /// <summary>
        /// アビリティボタンの参照
        /// </summary>
        private ModAbilityButton? CoordinateButton = null;

        /// <summary>
        /// 役職能力のコンストラクタ。
        /// </summary>
        /// <param name="player">割り当て対象のプレイヤー</param>
        /// <param name="isUsurped">能力が簒奪されている場合、true</param>
        public Ability(GamePlayer player, bool isUsurped) : base(player, isUsurped)
        {
            if (AmOwner)
            {
                // Coordinateボタンを作成
                CoordinateButton = NebulaAPI.Modules.AbilityButton(this, MyPlayer, Virial.Compat.VirtualKeyInput.Ability,
                    CoordinateCoolDownOption, "coordinate", null, 
                    null, _ => true)
                    .SetAsUsurpableButton(this);

                // ボタンをModAbilityButtonImplにキャストして設定
                if (CoordinateButton is Nebula.Modules.ScriptComponents.ModAbilityButtonImpl buttonImpl)
                {
                    // 自動クールダウンリセットを無効化（距離計算で設定するため）
                    buttonImpl.UseCoolDownSupport = false;
                    
                    // カスタムタイマーを作成
                    // visualMax=maxCooldownで動的クールダウンが正しく表示される
                    float maxCooldown = KillCooldownMaxOption;
                    float defaultCooldown = Coordinator.CoordinateCoolDownOption;
                    const float initialCooldown = 10f;  // ゲーム開始時は固定10秒（Among Usの標準動作）
                    NebulaPlugin.Log.Print($"Coordinator: Initial cooldown set to {initialCooldown}s (game start)");
                    var timer = new Nebula.Modules.ScriptComponents.AdvancedTimer(initialCooldown, maxCooldown)
                        .SetDefault(defaultCooldown)
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
                    
                    // クールダウンは ExecuteCoordinate で距離に応じて設定される
                };

                CoordinateButton.SetLabel("coordinate");
            }
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
            ShapeshifterMinigame prefab = null;
            var foundObjects = Resources.FindObjectsOfTypeAll(Il2CppType.Of<ShapeshifterMinigame>());
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
            minigame.transform.SetParent(Camera.main.transform, false);
            minigame.transform.localPosition = new Vector3(0f, 0f, -50f);

            // ※ShapeshifterMinigameのBeginは呼ばずに手動で構築する (ターゲット条件が異なるため)
            // 既存のBegin呼ぶと死体も含まれる可能性があるため

            // パネル配置用リスト
            Il2CppSystem.Collections.Generic.List<UiElement> controllerButtons = new Il2CppSystem.Collections.Generic.List<UiElement>();

            // パネル生成ループ
            // ShapeshifterMinigameの定数を参照したいが、インスタンスから値を取る
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
                // SetPlayer(int targetId, NetworkedPlayerInfo info, Action onClick)
                panel.SetPlayer(targetPlayer.PlayerId, targetPlayer.VanillaPlayer.Data, new Action(() => 
                {
                    SetPlayer(targetPlayer.PlayerId);
                }));

                // 名前の色設定 (インポスターなら赤、それ以外は白)
                panel.NameText.color = targetPlayer.VanillaPlayer.Data.Role.TeamType == RoleTeamTypes.Impostor ? Palette.ImpostorRed : Palette.White;

                // カーソル表示 (選択中のプレイヤーなら表示)
                if (SelectedTarget != null && SelectedTarget.PlayerId == targetPlayer.PlayerId)
                {
                    var cursor = new GameObject("Cursor");
                    cursor.transform.SetParent(minigame.transform);
                    cursor.transform.localPosition = new Vector3(0, 0, -5f);
                    var cursorSprite = cursor.AddComponent<SpriteRenderer>();
                    
                    if (MapBehaviour.Instance && MapBehaviour.Instance.HerePoint) 
                    {
                        cursorSprite.sprite = MapBehaviour.Instance.HerePoint.sprite;
                        cursorSprite.color = Color.cyan;
                    }
                }

                controllerButtons.Add(panel.Button); // Kept panel.Button as it's a UiElement, panel itself might not be.
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
                NebulaPlugin.Log.Print("Coordinator: Closing PlayerSelectMinigame");
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
            NebulaPlugin.Log.Print($"Coordinator: Target set to {SelectedTarget.Name}");
            
            // プレイヤー選択画面を閉じる
            ClosePlayerSelectMinigame();

            // マップを開く (HudManager経由)
            // OnOpenMapイベントでレイヤー生成をフックする
            HudManager.Instance.ToggleMapVisible(new MapOptions { Mode = MapOptions.Modes.Normal, AllowMovementWhileMapOpen = true });
        }

        // マップが開かれた時のイベント (Doppelgangerを参考)
        [Local]
        void OnOpenMap(AbstractMapOpenEvent ev)
        {
            NebulaPlugin.Log.Print($"Coordinator: OnOpenMap called. Event type: {ev?.GetType().Name}");
            
            // ターゲットが選択されていない、または死んでいる場合は何もしない
            if (SelectedTarget == null || MyPlayer.IsDead) 
            {
                NebulaPlugin.Log.Print($"Coordinator: OnOpenMap - early return. SelectedTarget: {SelectedTarget?.Name ?? "null"}, MyPlayer.IsDead: {MyPlayer.IsDead}");
                if (mapLayer != null) mapLayer.gameObject.SetActive(false);
                return;
            }

            // 通常マップイベントのみ対応
            if (ev is MapOpenNormalEvent && !IsUsurped && !MeetingHud.Instance)
            {
                NebulaPlugin.Log.Print("Coordinator: OnOpenMap triggered. Showing Target UI.");

                if (mapLayer == null)
                {
                    // レイヤー未生成なら作成 (Doppelganger方式)
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
            NebulaPlugin.Log.Print($"Coordinator: OnRoomSelected called with room = {room}, clickedPos = ({clickedWorldPos.x}, {clickedWorldPos.y})");
            ExecuteCoordinate(SelectedTarget, room, clickedWorldPos);
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
        /// 座標判定を実行してキルクールダウンを設定する処理
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
                    titleShower.SetText($"{target.PlayerName} は既に死んでいるようだ...", infoColor, 3f, true);
                }
                AmongUsUtil.PlayCustomFlash(infoColor, 0f, 0.5f, 0.4f, 0f);
                CloseAllScreens();
                return;
            }

            // 距離計算
            Vector2 targetPos = target.TruePosition;
            Vector2 myPos = MyPlayer.TruePosition;
            
            // 精度距離: クリック位置とターゲット位置の距離 (小さいほど良い)
            float accuracyDistance = Vector2.Distance(clickedWorldPos, targetPos);
            // 射程距離: 自分とターゲットの距離 (大きいほど良い)
            float rangeDistance = Vector2.Distance(myPos, targetPos);
            
            NebulaPlugin.Log.Print($"Coordinator: Accuracy={accuracyDistance:F2}m, Range={rangeDistance:F2}m");

            // キルクールダウン計算
            // 近距離 = ペナルティ（クールダウン増加）
            // 遠距離 = ボーナス（クールダウン減少）
            // 正確 = ボーナス（クールダウン減少）
            // 不正確 = ペナルティ（クールダウン増加）
            float baseCooldown = CoordinateCoolDownOption;
            float minCooldown = KillCooldownMinOption;
            float maxCooldown = KillCooldownMaxOption;
            
            NebulaPlugin.Log.Print($"Coordinator: base={baseCooldown}, min={minCooldown}, max={maxCooldown}");
            
            // 計算用スケール
            const float longRangeThreshold = 20f;   // これ以上で遠距離ボーナス最大（25->20に下げ）
            const float shortRangeThreshold = 5f;   // これ以下で近距離ペナルティ最大
            const float accuracyThreshold = 5f;     // これ以下で正確ボーナス最大
            const float inaccuracyThreshold = 15f;  // これ以上で不正確ペナルティ最大
            
            // 距離効果（比重を重くする）
            // 近距離（0-5m）: +10秒ペナルティ ～ 遠距離（20m+）: -17.5秒ボーナス
            float distanceEffect;
            if (rangeDistance < shortRangeThreshold)
            {
                // 近距離ペナルティ: 0m = +10秒, 5m = 0秒
                distanceEffect = (1f - rangeDistance / shortRangeThreshold) * 10f;
            }
            else
            {
                // 遠距離ボーナス: 5m = 0秒, 20m+ = -17.5秒（12.5->17.5に増加）
                distanceEffect = -Mathf.Min((rangeDistance - shortRangeThreshold) / (longRangeThreshold - shortRangeThreshold), 1f) * 17.5f;
            }
            
            // 精度効果
            // 正確（0-5m）: -5秒ボーナス ～ 不正確（15m+）: +7.5秒ペナルティ
            float accuracyEffect;
            if (accuracyDistance < accuracyThreshold)
            {
                // 正確ボーナス: 0m = -5秒, 5m = 0秒
                accuracyEffect = -(1f - accuracyDistance / accuracyThreshold) * 5f;
            }
            else
            {
                // 不正確ペナルティ: 5m = 0秒, 15m+ = +7.5秒
                accuracyEffect = Mathf.Min((accuracyDistance - accuracyThreshold) / (inaccuracyThreshold - accuracyThreshold), 1f) * 7.5f;
            }
            
            // 最終計算: base + 距離効果 + 精度効果
            float calculatedCooldown = baseCooldown + distanceEffect + accuracyEffect;
            
            // 範囲内に収める
            float finalCooldown = Mathf.Clamp(calculatedCooldown, minCooldown, maxCooldown);
            
            NebulaPlugin.Log.Print($"Coordinator: distanceEffect={distanceEffect:F2}, accuracyEffect={accuracyEffect:F2}");
            NebulaPlugin.Log.Print($"Coordinator: Calculated cooldown = {calculatedCooldown:F2}s, Final = {finalCooldown:F2}s");

            // ターゲットの現在部屋を判定
            // ShipStatus.Instance.FastRoomsを使用
            bool isCorrect = false;
            
            if (ShipStatus.Instance.FastRooms.TryGetValue(targetRoom, out var room) && room.roomArea != null)
            {
                // Collider2D.OverlapPoint で判定
                if (room.roomArea.OverlapPoint(target.TruePosition))
                {
                    isCorrect = true;
                }
            }

            // スコア計算（精度ベース: 0m = 5000pt, 10m = 0pt）
            const float maxScoreDistance = 10f;
            const int maxScore = 5000;
            int score = (int)(Mathf.Clamp01(1f - accuracyDistance / maxScoreDistance) * maxScore);
            
            NebulaPlugin.Log.Print($"Coordinator: Score = {score} (accuracy = {accuracyDistance:F2}m)");

            if (isCorrect)
            {
                NebulaPlugin.Log.Print($"Coordinator: Guess CORRECT! Killing {target.Name} with cooldown {finalCooldown:F2}s");

                // 正解：ターゲットをキル (RemoteKill)
                MyPlayer.MurderPlayer(target, PlayerState.Dead, EventDetail.Kill, KillParameter.RemoteKill, KillCondition.BothAlive);

                // スコア表示（緑色）+ フラッシュ
                var currentGame = NebulaAPI.CurrentGame;
                if (currentGame != null)
                {
                    var titleShower = currentGame.GetModule<Nebula.Game.TitleShower>();
                    if (titleShower != null)
                    {
                        titleShower.SetText($"{score} POINT!", new Color(0.2f, 1f, 0.4f), 3f, true);
                    }
                }
                Nebula.Utilities.AmongUsUtil.PlayCustomFlash(new Color(0.2f, 1f, 0.4f), 0f, 0.5f, 0.4f, 0f);

                // キル処理後にクールダウンを設定（遅延させることでMurderPlayerの内部リセットを回避）
                float cooldownToSet = finalCooldown;
                NebulaManager.Instance.ScheduleDelayAction(() =>
                {
                    if (CoordinateButton?.CoolDownTimer is Nebula.Modules.ScriptComponents.AdvancedTimer timerCorrect)
                    {
                        timerCorrect.SetVisualMax(cooldownToSet);
                        timerCorrect.Start(new float?(cooldownToSet));
                        NebulaPlugin.Log.Print($"Coordinator: Timer started with {cooldownToSet}s (delayed)");
                    }
                });

                // キル音を再生 (自分のみ)
                if (PlayerControl.LocalPlayer != null)
                {
                    SoundManager.Instance.PlaySound(PlayerControl.LocalPlayer.KillSfx, false, 0.8f);
                }
            }
            else
            {
                NebulaPlugin.Log.Print($"Coordinator: Guess WRONG! Target {target.Name} is not in {targetRoom}. Cooldown = {finalCooldown:F2}s");
                
                // スコア表示（赤色）+ フラッシュ
                var currentGame = NebulaAPI.CurrentGame;
                if (currentGame != null)
                {
                    var titleShower = currentGame.GetModule<Nebula.Game.TitleShower>();
                    if (titleShower != null)
                    {
                        titleShower.SetText($"{score} POINT...", new Color(1f, 0.3f, 0.3f), 3f, true);
                    }
                }
                Nebula.Utilities.AmongUsUtil.PlayCustomFlash(new Color(1f, 0.3f, 0.3f), 0f, 0.5f, 0.4f, 0f);
                
                // 不正解：ターゲットに通知 + 距離計算クールダウンを設定（通知がペナルティ）
                CoordinatorHelpers.RpcHolder.RpcNotifyTarget.Invoke(target.PlayerId);
                
                // 遅延させてクールダウンを設定
                float cooldownToSetWrong = finalCooldown;
                NebulaManager.Instance.ScheduleDelayAction(() =>
                {
                    if (CoordinateButton?.CoolDownTimer is Nebula.Modules.ScriptComponents.AdvancedTimer timerWrong)
                    {
                        timerWrong.SetVisualMax(cooldownToSetWrong);
                        timerWrong.Start(new float?(cooldownToSetWrong));
                        NebulaPlugin.Log.Print($"Coordinator: Timer started with {cooldownToSetWrong}s (delayed)");
                    }
                });
            }

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
        /// 会議開始時に画面を閉じる
        /// </summary>
        [Local]
        void OnMeetingStart(MeetingStartEvent ev)
        {
            CloseAllScreens();
        }

        /// <summary>
        /// 会議終了後（タスクフェーズ再開時）にクールダウンをリセット
        /// </summary>
        [Local]
        void OnTaskPhaseRestart(TaskPhaseRestartEvent ev)
        {
            if (CoordinateButton?.CoolDownTimer is Nebula.Modules.ScriptComponents.AdvancedTimer timer)
            {
                float cooldown = MyPlayer.TeamKillCooldown;
                timer.SetVisualMax(cooldown);
                timer.Start(new float?(cooldown));
                NebulaPlugin.Log.Print($"Coordinator: Cooldown reset after meeting to {cooldown}s");
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
                // 引数が必要
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
