
namespace MoreRolesInPolus.Roles.Imposter;

using Nebula.Modules;
using Nebula.Modules.MetaWidget;
using Nebula.Utilities;
using Nebula.Behavior;
using TMPro;
using UnityEngine;
using MoreRolesInPolus.Helpers;

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
        private GamePlayer? SelectedTarget = null;

        /// <summary>
        /// プレイヤー選択画面のMetaScreen参照
        /// </summary>
        private MetaScreen? PlayerSelectScreen = null;

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
        /// プレイヤー選択画面を開く処理
        /// </summary>
        void OpenPlayerSelectScreen()
        {
            // 生存している他のプレイヤーのリストを取得（自分以外）
            var alivePlayers = GamePlayer.AllPlayers.Where(p => !p.IsDead && !p.AmOwner).ToList();
            
            // プレイヤー名でソート
            alivePlayers.Sort((p1, p2) => p1.PlayerName.CompareTo(p2.PlayerName));

            if (alivePlayers.Count == 0) return;

            // 画面生成
            var screen = MetaScreen.GenerateWindow(new Virial.Compat.Vector2(7.6f, 4.2f), HudManager.Instance.transform, new Vector3(0, 0, -50f), true, false);

            MetaWidgetOld widget = new();
            MetaWidgetOld inner = new();

            // プレイヤーボタン生成 (グリッド配置)
            inner.Append(alivePlayers, (targetPlayer) => 
            {
                return new CombinedWidgetOld(
                    new MetaWidgetOld.HorizonalMargin(0.1f),
                    new MetaWidgetOld.Button(() =>
                    {
                        // 選択処理
                        SelectedTarget = targetPlayer;
                        if (PlayerSelectScreen != null)
                        {
                            PlayerSelectScreen.CloseScreen();
                            PlayerSelectScreen = null;
                        }
                        OpenRoomSelectScreen();
                    }, ButtonAttribute)
                    {
                        RawText = targetPlayer.PlayerName,
                        TextHorizonotalExtraMargin = 0.15f,
                        PostBuilder = (button, renderer, text) =>
                        {
                            renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                            // ボタンテキストの位置調整
                            button.transform.localPosition += new Vector3(0.05f, 0f, 0f);
                            text.transform.localPosition += new Vector3(0.072f, 0f, 0f);

                            // プレイヤーアバター表示
                            var display = VanillaAsset.GetPlayerDisplay();
                            display.transform.SetParent(button.transform);
                            display.transform.localPosition = new Vector3(-0.65f, -0.2f, -1f);
                            display.transform.localScale = new Vector3(0.45f, 0.45f, 1f);
                            
                            var playerDisplay = display.GetComponent<PlayerDisplay>();
                            if (playerDisplay != null)
                            {
                                // 現在の見た目を反映
                                var control = Nebula.Utilities.Helpers.GetPlayer(targetPlayer.PlayerId);
                                if (control != null)
                                {
                                    playerDisplay.UpdateFromPlayerOutfit(control, false, false);
                                    
                                    // 名前は非表示
                                    playerDisplay.Cosmetics.ToggleName(false);
                                }
                            }

                            // レイヤー設定（UIマスクで隠れるように）
                            foreach (var r in display.GetComponentsInChildren<SpriteRenderer>())
                            {
                                r.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                                r.sortingLayerID = renderer.sortingLayerID;
                                r.sortingOrder = renderer.sortingOrder + 1;
                            }
                        }
                    }
                );
            }, 4, -1, 0, 0.59f);

            // スクロールビューに格納
            MetaWidgetOld.ScrollView scroller = new(new(6.9f, 3.8f), inner, true) 
            { 
                Alignment = IMetaWidgetOld.AlignmentOption.Center 
            };
            
            widget.Append(scroller);

            // タイトル
            widget.Append(new MetaWidgetOld.Text(TextAttributeOld.BoldAttr) 
            { 
                MyText = new RawTextComponent(Language.Translate("coordinator.selectPlayer")), 
                Alignment = IMetaWidgetOld.AlignmentOption.Center 
            });

            screen.SetWidget(widget);
            PlayerSelectScreen = screen;
        }

        /// <summary>
        /// 部屋選択画面を開く処理（アドミンマップ形式）
        /// </summary>
        void OpenRoomSelectScreen()
        {
            if (SelectedTarget == null) return;

            // アドミンマップの部屋座標を取得
            var mapData = Nebula.Map.MapData.GetCurrentMapData();
            var adminRooms = mapData.AdminRooms;

            if (adminRooms == null || adminRooms.Length == 0)
            {
                // フォールバック: リスト形式
                OpenRoomSelectScreenAsList();
                return;
            }

            // マップ画面を生成
            var screen = MetaScreen.GenerateWindow(new Virial.Compat.Vector2(7.5f, 4.5f), HudManager.Instance.transform, UnityEngine.Vector3.zero, true, false);

            MetaWidgetOld widget = new();

            // タイトル
            widget.Append(new MetaWidgetOld.Text(TextAttributeOld.BoldAttr) 
            { 
                RawText = Language.Translate("coordinator.selectRoom") + $" - Target: {SelectedTarget!.PlayerName}", 
                Alignment = IMetaWidgetOld.AlignmentOption.Center 
            });
            widget.Append(new MetaWidgetOld.VerticalMargin(0.35f));

            // マップ画像と部屋ボタンを配置
            var capturedTarget = SelectedTarget;
            var capturedScreen = screen;
            var roomButtons = adminRooms.Select(roomInfo =>
            {
                var roomType = roomInfo.room;
                string roomName = GetRoomNameFromSystemType(roomType);

                return ((IMetaParallelPlacableOld button, UnityEngine.Vector2 pos))(
                    new MetaWidgetOld.WrappedWidget(
                        NebulaAPI.GUI.Button(GUIAlignment.Center, 
                            NebulaAPI.GUI.GetAttribute(Virial.Text.AttributeAsset.OverlayContent), 
                            NebulaAPI.GUI.RawTextComponent(roomName),
                            (button) =>
                            {
                                // 部屋が選択された - 即座に判定実行
                                ExecuteCoordinate(capturedTarget!, roomName);

                                // 画面を閉じる
                                capturedScreen?.CloseScreen();
                                RoomSelectScreen = null;
                                SelectedTarget = null;
                            }, 
                            margin: 0.14f)
                    ), 
                    roomInfo.pos
                );
            });

            widget.Append(MetaWidgetOld.Image.AsMapImage(AmongUsUtil.CurrentMapId, 5.6f, roomButtons, 0xFFFFFFF));

            screen.SetWidget(widget);

            // 閉じられた時の処理
            RoomSelectScreen = screen;
        }

        /// <summary>
        /// 部屋選択画面をリスト形式で開く（フォールバック用）
        /// </summary>
        void OpenRoomSelectScreenAsList()
        {
            if (SelectedTarget == null) return;

            // アドミンマップの部屋名リストを取得
            var mapData = Nebula.Map.MapData.GetCurrentMapData();
            var adminRooms = mapData.AdminRooms;
            
            if (adminRooms == null || adminRooms.Length == 0) return;

            List<GUIWidget> roomWidgets = new();

            foreach (var roomInfo in adminRooms)
            {
                var roomType = roomInfo.room;
                string roomName = GetRoomNameFromSystemType(roomType);
                var capturedTarget = SelectedTarget;
                
                var roomButton = NebulaAPI.GUI.Button(GUIAlignment.Center, 
                    NebulaAPI.GUI.GetAttribute(Virial.Text.AttributeAsset.OverlayContent), 
                    NebulaAPI.GUI.RawTextComponent(roomName),
                    (button) =>
                    {
                        ExecuteCoordinate(capturedTarget, roomName);
                        
                        if (RoomSelectScreen != null)
                        {
                            RoomSelectScreen.CloseScreen();
                            RoomSelectScreen = null;
                        }
                        SelectedTarget = null;
                    });

                roomWidgets.Add(roomButton);
            }

            var scrollView = new GUIScrollView(GUIAlignment.Center, new Virial.Compat.Vector2(4.5f, 3f), 
                () => NebulaAPI.GUI.VerticalHolder(GUIAlignment.Center, roomWidgets.ToArray()));

            var cancelButton = NebulaAPI.GUI.Button(GUIAlignment.Center,
                NebulaAPI.GUI.GetAttribute(Virial.Text.AttributeAsset.OverlayContent),
                NebulaAPI.GUI.LocalizedTextComponent("ui.button.cancel"),
                (button) =>
                {
                    if (RoomSelectScreen != null)
                    {
                        RoomSelectScreen.CloseScreen();
                        RoomSelectScreen = null;
                    }
                    SelectedTarget = null;
                });

            var mainContent = NebulaAPI.GUI.VerticalHolder(GUIAlignment.Center,
                NebulaAPI.GUI.LocalizedText(GUIAlignment.Center, NebulaAPI.GUI.GetAttribute(Virial.Text.AttributeAsset.OverlayTitle), "coordinator.selectRoom"),
                NebulaAPI.GUI.RawText(GUIAlignment.Center, NebulaAPI.GUI.GetAttribute(Virial.Text.AttributeAsset.OverlayContent), $"Target: {SelectedTarget!.PlayerName}"),
                NebulaAPI.GUI.VerticalMargin(0.1f),
                scrollView,
                NebulaAPI.GUI.VerticalMargin(0.1f),
                cancelButton
            );

            var screen = MetaScreen.GenerateWindow(new Virial.Compat.Vector2(7.5f, 4.5f), HudManager.Instance.transform, UnityEngine.Vector3.zero, true, true);
            screen.SetWidget(mainContent, out var _);
            RoomSelectScreen = screen;
        }

        /// <summary>
        /// SystemTypesから部屋名を取得するヘルパー
        /// </summary>
        /// <param name="roomType">部屋のSystemTypes</param>
        /// <returns>部屋名</returns>
        string GetRoomNameFromSystemType(SystemTypes roomType)
        {
            PlainShipRoom room;
            if (ShipStatus.Instance.FastRooms.TryGetValue(roomType, out room) && room.roomArea != null)
            {
                var center = room.roomArea.bounds.center;
                return NebulaAPI.CurrentGame?.CurrentMap?.GetRoomName(new Virial.Compat.Vector2(center.x, center.y), false, false, false) ?? roomType.ToString();
            }
            return roomType.ToString();
        }

        /// <summary>
        /// 座標判定を実行してキルを試みる処理
        /// </summary>
        /// <param name="target">ターゲットプレイヤー</param>
        /// <param name="guessedRoom">推測した部屋名</param>
        void ExecuteCoordinate(GamePlayer target, string guessedRoom)
        {
            if (target.IsDead) return;

            // ターゲットの現在位置を取得
            UnityEngine.Vector2 targetPos = target.TruePosition;

            // ターゲットが実際にいる部屋を取得
            var actualRoom = NebulaAPI.CurrentGame?.CurrentMap?.GetRoomName(targetPos, false, false, false);

            // 判定：推測した部屋と実際の部屋が一致するか
            bool isCorrect = actualRoom == guessedRoom;

            if (isCorrect)
            {
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
                // 不正解：ターゲットに通知
                CoordinatorHelpers.RpcHolder.RpcNotifyTarget.Invoke(target.PlayerId);
            }
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
        void CloseAllScreens()
        {
            if (PlayerSelectScreen != null)
            {
                PlayerSelectScreen.CloseScreen();
                PlayerSelectScreen = null;
            }
            if (RoomSelectScreen != null)
            {
                RoomSelectScreen.CloseScreen();
                RoomSelectScreen = null;
            }
            SelectedTarget = null;
        }
    }
}
