
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
    private Coordinator() : base("coordinator", new(Palette.ImpostorRed), RoleCategory.ImpostorRole, NebulaTeams.ImpostorTeam, [CoordinateCoolDownOption])
    {
        ConfigurationHolder?.AddTags(ConfigurationTags.TagFunny);
    }

    /// <summary>
    /// ロビーで変更できる設定を用意します。ゲーム中で編集できるように、すぐ上のコンストラクタで役職のオプションに追加します。
    /// </summary>
    static private readonly FloatConfiguration CoordinateCoolDownOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.coordinateCoolDown", (0f, 60f, 2.5f), 25f, FloatConfigurationDecorator.Second);

    /// <summary>
    /// 役職の情報を用意します。
    /// </summary>
    static public readonly Coordinator MyRole = new();

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
    public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
    {
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

                // ボタンクリック時の処理
                CoordinateButton.OnClick = (button) =>
                {
                    // プレイヤー選択画面を開く
                    OpenPlayerSelectScreen();
                    
                    // クールダウン開始
                    button.StartCoolDown();
                };

                CoordinateButton.SetLabel("coordinate");
            }
        }

        /// <summary>
        /// ボタンのスタイル定義
        /// </summary>
        static TextAttributeOld ButtonAttribute = new TextAttributeOld(TextAttributeOld.BoldAttr) { Size = new(1.05f, 0.3f), Alignment = TMPro.TextAlignmentOptions.Center, FontMaterial = VanillaAsset.StandardMaskedFontMaterial }.EditFontSize(2f, 1f, 2f);

        /// <summary>
        /// ターゲットカーソル画像
        /// </summary>
        private static Nebula.Utilities.SpriteLoader targetImage = Nebula.Utilities.SpriteLoader.FromResource("Nebula.Resources.SniperGuide.png", 100f);

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
                UnityEngine.Debug.Log("Coordinator: Closing PlayerSelectMinigame");
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
            UnityEngine.Debug.Log($"Coordinator: Target set to {SelectedTarget.Name}");
            
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
            // ターゲットが選択されていない、または死んでいる場合は何もしない
            // MyPlayer.Data.IsDead -> MyPlayer.IsDead (GamePlayer wrapper uses IsDead property directly)
            if (SelectedTarget == null || MyPlayer.IsDead) 
            {
                if (mapLayer != null) mapLayer.gameObject.SetActive(false);
                return;
            }

            // 通常マップイベントのみ対応
            if (ev is MapOpenNormalEvent && !IsUsurped && !MeetingHud.Instance)
            {
                UnityEngine.Debug.Log("Coordinator: OnOpenMap triggered. Showing Target UI.");

                if (mapLayer == null)
                {
                    // レイヤー未生成なら作成 (Doppelganger方式)
                    mapLayer = UnityHelper.CreateObject<CoordinatorMapLayer>("CoordinatorLayer", MapBehaviour.Instance.transform, new Vector3(0f, 0f, -1f), null);
                    mapLayer.Initialize((room) => 
                    {
                        ExecuteCoordinate(SelectedTarget, room);
                    });
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
        /// 座標判定を実行してキルを試みる処理
        /// </summary>
        /// <param name="target">ターゲットプレイヤー</param>
        /// <param name="targetRoom">推測した部屋タイプ</param>
        void ExecuteCoordinate(GamePlayer target, SystemTypes targetRoom)
        {
            if (target.IsDead) return;

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

            if (isCorrect)
            {
                UnityEngine.Debug.Log($"Coordinator: Guess CORRECT! Killing {target.Name} in {targetRoom}");

                // 正解：ターゲットをキル (RemoteKill)
                MyPlayer.MurderPlayer(target, PlayerState.Dead, EventDetail.Kill, KillParameter.RemoteKill, KillCondition.BothAlive);

                // キル音を再生 (自分のみ)
                if (PlayerControl.LocalPlayer != null)
                {
                    SoundManager.Instance.PlaySound(PlayerControl.LocalPlayer.KillSfx, false, 0.8f);
                }
            }
            else
            {
                UnityEngine.Debug.Log($"Coordinator: Guess WRONG! Target {target.Name} is not in {targetRoom}");
                
                // 不正解：ターゲットに通知
                CoordinatorHelpers.RpcHolder.RpcNotifyTarget.Invoke(target.PlayerId);
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
