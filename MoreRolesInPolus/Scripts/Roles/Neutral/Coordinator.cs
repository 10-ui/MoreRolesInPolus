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
using MoreRolesInPolus.Helpers;
using Il2CppInterop.Runtime;
using Virial.Events.Game.Minimap;
using Virial.Runtime;

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
    static private readonly FloatConfiguration CooldownMinOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.cooldownMin", (2.5f, 30f, 2.5f), 10f, FloatConfigurationDecorator.Second);
    
    static private readonly FloatConfiguration CooldownMaxOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.cooldownMax", (10f, 60f, 5f), 40f, FloatConfigurationDecorator.Second);
    
    static private readonly BoolConfiguration OverrideInitialCooldownOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.overrideInitialCooldown", false);
    
    static private readonly FloatConfiguration InitialCooldownOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.initialCooldown", (5f, 60f, 2.5f), 15f, FloatConfigurationDecorator.Second, () => OverrideInitialCooldownOption);
    
    static private readonly IntegerConfiguration PointsMultiplierOption = NebulaAPI.Configurations.Configuration("options.role.coordinator.pointsMultiplier", (1, 8), 4);

    public static int PointsToWin => PointsMultiplierOption * 5000;

    static public Coordinator MyRole = new();

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

        bool RuntimeRole.CanUseVent => true;
        bool RuntimeRole.HasImpostorVision => true;
        bool RuntimeRole.IgnoreBlackout => true;
        private GamePlayer? SelectedTarget = null;
        private MetaScreen? RoomSelectScreen = null;
        private ModAbilityButton? CoordinateButton = null;
        private int TotalScore = 0;

        static private readonly Image CoordinateSprite = NebulaAPI.AddonAsset.GetResource("Neutral/Coordinator/CoordinateButton.png")!.AsImage(115f)!;

        /// <summary>
        /// 能力発動可能エリア（OpportunistのTaskAreaと同じ矩形表現）
        /// </summary>
        private class ActivationArea
        {
            public SystemTypes Room { get; private set; }
            public string? LocationNameKey { get; private set; }
            public Vector2 Min { get; private set; }
            public Vector2 Max { get; private set; }

            public ActivationArea(SystemTypes room, string? locationNameKey, Vector2 min, Vector2 max)
            {
                Room = room;
                LocationNameKey = locationNameKey;
                Min = min;
                Max = max;
            }

            public bool CheckPosition(Vector2 position)
            {
                return Min.x < position.x && position.x < Max.x && Min.y < position.y && position.y < Max.y;
            }
        }

        static private ActivationArea CreateActivationArea(SystemTypes room, Vector2 center, float radius)
        {
            return new ActivationArea(room, null, new Vector2(center.x - radius, center.y - radius), new Vector2(center.x + radius, center.y + radius));
        }

        static private ActivationArea CreateActivationArea(SystemTypes room, string locationNameKey, Vector2 center, float radius)
        {
            return new ActivationArea(room, locationNameKey, new Vector2(center.x - radius, center.y - radius), new Vector2(center.x + radius, center.y + radius));
        }

        static private ActivationArea CreateActivationArea(SystemTypes room, float x1, float x2, float y1, float y2)
        {
            return new ActivationArea(room, null, new Vector2((x1 < x2) ? x1 : x2, (y1 < y2) ? y1 : y2), new Vector2((x1 < x2) ? x2 : x1, (y1 < y2) ? y2 : y1));
        }

        /// <summary>
        /// Opportunistと同じエリア群（マップ別）
        /// </summary>
        static private readonly ActivationArea[][] ActivationAreasByMap = new ActivationArea[][]
        {
            new ActivationArea[]
            {
                CreateActivationArea(SystemTypes.Nav, new Vector2(18f, -4.6f), 2.5f),
                CreateActivationArea(SystemTypes.LifeSupp, new Vector2(6.3f, -4.1f), 1.6f),
                CreateActivationArea(SystemTypes.Weapons, new Vector2(9.5f, 2.3f), 2.5f),
                CreateActivationArea(SystemTypes.Cafeteria, new Vector2(-1f, 8.7f), 3.5f),
                CreateActivationArea(SystemTypes.Shields, new Vector2(9.7f, -12.4f), 2.5f),
                CreateActivationArea(SystemTypes.Storage, new Vector2(-0.2f, -16.5f), 1.5f),
                CreateActivationArea(SystemTypes.Storage, new Vector2(-8.8f, -8f), 2f),
                CreateActivationArea(SystemTypes.LowerEngine, new Vector2(-17.7f, -13f), 2f),
                CreateActivationArea(SystemTypes.Reactor, new Vector2(-22.5f, -2.4f), 2f),
                CreateActivationArea(SystemTypes.MedBay, new Vector2(-7f, -5f), 1.8f)
            },
            new ActivationArea[]
            {
                CreateActivationArea(SystemTypes.Reactor, new Vector2(2.5f, 11.9f), 1f),
                CreateActivationArea(SystemTypes.Laboratory, new Vector2(10.7f, 13.3f), 1.6f),
                CreateActivationArea(SystemTypes.Launchpad, new Vector2(-4.3f, 2.1f), 2.4f),
                CreateActivationArea(SystemTypes.MedBay, new Vector2(15.7f, -1.6f), 1.8f),
                CreateActivationArea(SystemTypes.Balcony, new Vector2(19.1f, -2.6f), 1.5f),
                CreateActivationArea(SystemTypes.Storage, new Vector2(19.4f, 4.3f), 1.7f),
                CreateActivationArea(SystemTypes.Cafeteria, new Vector2(27.8f, 4.6f), 1.1f),
                CreateActivationArea(SystemTypes.Admin, new Vector2(21.9f, 19.2f), 2f),
                CreateActivationArea(SystemTypes.Greenhouse, new Vector2(17.8f, 25.8f), 2f),
                CreateActivationArea(SystemTypes.Office, new Vector2(14.7f, 19.4f), 1.8f)
            },
            new ActivationArea[]
            {
                CreateActivationArea(SystemTypes.Security, new Vector2(2.8f, -12f), 1f),
                CreateActivationArea(SystemTypes.Comms, new Vector2(11.5f, -16.8f), 1f),
                CreateActivationArea(SystemTypes.Laboratory, new Vector2(26.8f, -7.7f), 1f),
                CreateActivationArea(SystemTypes.Laboratory, new Vector2(33.7f, -10f), 1f),
                CreateActivationArea(SystemTypes.Specimens, new Vector2(36.6f, -20.8f), 2f),
                CreateActivationArea(SystemTypes.Admin, new Vector2(21.2f, -25.7f), 1.7f),
                CreateActivationArea(SystemTypes.LifeSupp, new Vector2(0.9f, -20.7f), 1.2f),
                CreateActivationArea(SystemTypes.Outside, "polus.outside.ejection", new Vector2(32.4f, -15.7f), 1.9f),
                CreateActivationArea(SystemTypes.Storage, new Vector2(20.6f, -11.6f), 1f),
                CreateActivationArea(SystemTypes.Outside, "polus.outside.comms", new Vector2(7.9f, -16f), 1.5f)
            },
            Array.Empty<ActivationArea>(),
            new ActivationArea[]
            {
                CreateActivationArea(SystemTypes.MeetingRoom, new Vector2(16f, 15f), 1.5f),
                CreateActivationArea(SystemTypes.MainHall, new Vector2(9.2f, 2.5f), 1.5f),
                CreateActivationArea(SystemTypes.MainHall, new Vector2(6.1f, 3.5f), 1f),
                CreateActivationArea(SystemTypes.MainHall, new Vector2(12.4f, 2.5f), 1.5f),
                CreateActivationArea(SystemTypes.Showers, 20.2f, 24.9f, 1.9f, 3.6f),
                CreateActivationArea(SystemTypes.Lounge, 28.4f, 29.8f, 6.7f, 8.2f),
                CreateActivationArea(SystemTypes.Lounge, 30f, 31.5f, 6.7f, 8.2f),
                CreateActivationArea(SystemTypes.Lounge, 31.5f, 33f, 6.7f, 8.2f),
                CreateActivationArea(SystemTypes.Lounge, 33f, 34.5f, 6.7f, 8.2f),
                CreateActivationArea(SystemTypes.CargoBay, new Vector2(37.1f, -3.1f), 1.5f),
                CreateActivationArea(SystemTypes.Ventilation, new Vector2(27.5f, -0.7f), 2.4f),
                CreateActivationArea(SystemTypes.Medical, new Vector2(25.2f, -9.2f), 1.5f),
                CreateActivationArea(SystemTypes.Electrical, new Vector2(19.3f, -6.5f), 1.2f),
                CreateActivationArea(SystemTypes.Electrical, new Vector2(16.3f, -6.3f), 1.4f),
                CreateActivationArea(SystemTypes.Electrical, new Vector2(13.3f, -6.3f), 1.4f),
                CreateActivationArea(SystemTypes.Electrical, new Vector2(10.3f, -6.3f), 1.4f),
                CreateActivationArea(SystemTypes.Electrical, new Vector2(13.2f, -8.8f), 1.2f),
                CreateActivationArea(SystemTypes.Electrical, new Vector2(16.3f, -8.8f), 1.2f),
                CreateActivationArea(SystemTypes.Electrical, new Vector2(19.3f, -8.8f), 1.2f),
                CreateActivationArea(SystemTypes.Electrical, new Vector2(19.3f, -11.2f), 1.4f),
                CreateActivationArea(SystemTypes.Electrical, new Vector2(16.3f, -11.2f), 1.4f),
                CreateActivationArea(SystemTypes.Security, new Vector2(9.9f, -15.6f), 1.5f),
                CreateActivationArea(SystemTypes.HallOfPortraits, -1.5f, 3f, -10.9f, -13.9f),
                CreateActivationArea(SystemTypes.ViewingDeck, new Vector2(-13.6f, -15.6f), 1.5f),
                CreateActivationArea(SystemTypes.Armory, new Vector2(-14.4f, -8.7f), 1.5f),
                CreateActivationArea(SystemTypes.Cockpit, new Vector2(-22.8f, -0.3f), 2f)
            },
            new ActivationArea[]
            {
                CreateActivationArea(SystemTypes.MiningPit, new Vector2(13.7f, 9.8f), 1.2f),
                CreateActivationArea(SystemTypes.Lookout, new Vector2(7.3f, 0.7f), 1.5f),
                CreateActivationArea(SystemTypes.UpperEngine, new Vector2(22.4f, 3f), 1.3f),
                CreateActivationArea(SystemTypes.Highlands, new Vector2(15.4f, 2.6f), 2.5f),
                CreateActivationArea(SystemTypes.Highlands, 17.4f, 22.8f, 6.1f, 8.8f),
                CreateActivationArea(SystemTypes.Comms, new Vector2(22.6f, 16.2f), 4f),
                CreateActivationArea(SystemTypes.SleepingQuarters, new Vector2(2.3f, -1.6f), 1.5f),
                CreateActivationArea(SystemTypes.Jungle, new Vector2(13.6f, -15.5f), 2f),
                CreateActivationArea(SystemTypes.Reactor, new Vector2(21.9f, -7.4f), 2f),
                CreateActivationArea(SystemTypes.Laboratory, new Vector2(-4f, -9.3f), 1.8f),
                CreateActivationArea(SystemTypes.Kitchen, -18.2f, -12.6f, -10.6f, -8.5f),
                CreateActivationArea(SystemTypes.FishingDock, new Vector2(-22.5f, -6.8f), 1.5f),
                CreateActivationArea(SystemTypes.RecRoom, new Vector2(-19.9f, -0.4f), 2f),
                CreateActivationArea(SystemTypes.Cafeteria, new Vector2(-16.4f, 6.8f), 3f)
            }
        };

        /// <summary>
        /// 1フェーズで有効にするエリア数
        /// </summary>
        private const int ActiveAreaCountPerPhase = 3;

        /// <summary>
        /// 現在有効な発動エリアのインデックス一覧
        /// </summary>
        private readonly List<int> CurrentActivationAreaIndexes = new();

        /// <summary>
        /// 現在の発動エリアが紐づくマップID
        /// </summary>
        private int CurrentActivationAreaMapId = -1;

        /// <summary>
        /// 有効エリア可視化用のレンダラー一覧
        /// </summary>
        private readonly List<SpriteRenderer> ActivationAreaRenderers = new();

        private ActivationArea[] GetCurrentMapActivationAreas()
        {
            int currentMapId = (int)AmongUsUtil.CurrentMapId;
            if (currentMapId < 0 || currentMapId >= ActivationAreasByMap.Length) return Array.Empty<ActivationArea>();
            return ActivationAreasByMap[currentMapId];
        }

        private bool TryGetCurrentActivationAreas(out ActivationArea[] activationAreas)
        {
            activationAreas = Array.Empty<ActivationArea>();
            var mapAreas = GetCurrentMapActivationAreas();
            if (mapAreas.Length == 0) return false;
            if (CurrentActivationAreaMapId != (int)AmongUsUtil.CurrentMapId) return false;
            if (CurrentActivationAreaIndexes.Count == 0) return false;
            if (CurrentActivationAreaIndexes.Any(index => index < 0 || index >= mapAreas.Length)) return false;

            activationAreas = CurrentActivationAreaIndexes.Select(index => mapAreas[index]).ToArray();
            return activationAreas.Length > 0;
        }

        private void EnsureCurrentActivationAreaInitialized()
        {
            int currentMapId = (int)AmongUsUtil.CurrentMapId;
            var mapAreas = GetCurrentMapActivationAreas();
            if (mapAreas.Length == 0)
            {
                CurrentActivationAreaIndexes.Clear();
                CurrentActivationAreaMapId = currentMapId;
                return;
            }

            bool shouldInitialize =
                CurrentActivationAreaMapId != currentMapId ||
                CurrentActivationAreaIndexes.Count == 0 ||
                CurrentActivationAreaIndexes.Any(index => index < 0 || index >= mapAreas.Length);

            if (!shouldInitialize) return;

            RerollCurrentActivationAreas(false);
        }

        private void RerollCurrentActivationAreas(bool shouldNotify)
        {
            int currentMapId = (int)AmongUsUtil.CurrentMapId;
            var mapAreas = GetCurrentMapActivationAreas();
            if (mapAreas.Length == 0)
            {
                CurrentActivationAreaIndexes.Clear();
                CurrentActivationAreaMapId = currentMapId;
                ClearActivationAreaVisualizations();
                return;
            }

            CurrentActivationAreaMapId = currentMapId;
            CurrentActivationAreaIndexes.Clear();

            var candidateIndexes = Enumerable.Range(0, mapAreas.Length).ToList();
            for (int i = 0; i < candidateIndexes.Count; i++)
            {
                int randomIndex = UnityEngine.Random.Range(i, candidateIndexes.Count);
                (candidateIndexes[i], candidateIndexes[randomIndex]) = (candidateIndexes[randomIndex], candidateIndexes[i]);
            }

            int pickCount = UnityEngine.Mathf.Min(ActiveAreaCountPerPhase, candidateIndexes.Count);
            for (int i = 0; i < pickCount; i++)
            {
                CurrentActivationAreaIndexes.Add(candidateIndexes[i]);
            }

            UpdateActivationAreaVisualizations();
            if (shouldNotify) ShowActivationAreaInfo();
        }

        private void ShowActivationAreaInfo()
        {
            if (!TryGetCurrentActivationAreas(out var activationAreas) || activationAreas.Length == 0) return;
            var titleShower = NebulaAPI.CurrentGame?.GetModule<TitleShower>();
            if (titleShower == null) return;

            string roomText = string.Join(", ", activationAreas.Select(GetActivationAreaDisplayName).Distinct());
            titleShower.SetText($"[Coordinator] 発動エリア: {roomText}", CoordinatorTeamInfo.MyTeam!.UnityColor, 2.5f, true);
        }

        private string GetActivationAreaDisplayName(ActivationArea area)
        {
            if (!string.IsNullOrWhiteSpace(area.LocationNameKey))
            {
                string key = $"role.coordinator.area.{area.LocationNameKey}";
                var localized = Language.Find(key);
                if (!string.IsNullOrEmpty(localized)) return localized;
            }

            return GetRoomNameFromSystemType(area.Room);
        }

        private string GetActivationAreaShortName(ActivationArea area)
        {
            if (area.LocationNameKey == "polus.outside.ejection") return "屋外(溶岩)";
            if (area.LocationNameKey == "polus.outside.comms") return "屋外(通信)";

            string roomName = GetActivationAreaDisplayName(area);
            return roomName.Replace("エリア", string.Empty).Replace("ルーム", string.Empty);
        }

        private void ClearActivationAreaVisualizations()
        {
            foreach (var renderer in ActivationAreaRenderers)
            {
                if (renderer != null) Object.Destroy(renderer.gameObject);
            }
            ActivationAreaRenderers.Clear();
        }

        private void UpdateActivationAreaVisualizations()
        {
            ClearActivationAreaVisualizations();

            if (!TryGetCurrentActivationAreas(out var activationAreas) || activationAreas.Length == 0) return;
            foreach (var activationArea in activationAreas)
            {
                Vector2 center = (activationArea.Min + activationArea.Max) * 0.5f;
                Vector2 size = activationArea.Max - activationArea.Min;
                var renderer = Nebula.Utilities.Helpers.DisplayArea(center, CoordinatorTeamInfo.MyTeam!.UnityColor, size);
                ActivationAreaRenderers.Add(renderer);
            }
        }

        private bool IsInActivationArea(Vector2 position)
        {
            EnsureCurrentActivationAreaInitialized();
            if (!TryGetCurrentActivationAreas(out var activationAreas) || activationAreas.Length == 0) return false;
            return activationAreas.Any(area => area.CheckPosition(position));
        }


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
                    null, _ => IsInActivationArea(MyPlayer.TruePosition));

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
                EnsureCurrentActivationAreaInitialized();
                UpdateActivationAreaVisualizations();
            }
        }

        /// <summary>
        /// タスク欄を上書きし、通常タスクを非表示にして
        /// Coordinator用の情報（スコアと発動可能エリア）だけを表示する。
        /// </summary>
        [Local]
        void UpdateCoordinatorTaskText(PlayerTaskTextLocalEvent ev)
        {
            EnsureCurrentActivationAreaInitialized();

            string scoreText = $"[Coordinator] Score: {TotalScore}/{PointsToWin}";
            string areaText = "Area: -";
            if (TryGetCurrentActivationAreas(out var activationAreas) && activationAreas.Length > 0)
            {
                string roomText = string.Join(" / ", activationAreas
                    .Select(GetActivationAreaShortName)
                    .Distinct());
                areaText = $"Area: {roomText}";
            }

            // 本文を2行に固定して置き換える（縦オーバー対策）
            ev.ReplaceBody($"{scoreText}\n{areaText}".Color(CoordinatorTeamInfo.MyTeam!.UnityColor));
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
            HudManager.Instance.ToggleMapVisible(new MapOptions { Mode = MapOptions.Modes.Normal, AllowMovementWhileMapOpen = true, ShowLivePlayerPosition = true });
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
                bool shouldShowScoreTitleShower = TotalScore < PointsToWin && !HasTriggeredGoalMeeting;

                // スコア表示（緑色）+ フラッシュ
                var currentGame = NebulaAPI.CurrentGame;
                if (shouldShowScoreTitleShower && currentGame != null)
                {
                    var titleShower = currentGame.GetModule<Nebula.Game.TitleShower>();
                    if (titleShower != null)
                    {
                        titleShower.SetText($"+{score} POINT! (Total: {TotalScore}/{PointsToWin})", new Color(0.2f, 1f, 0.4f), 3f, true);
                    }
                }
                Nebula.Utilities.AmongUsUtil.PlayCustomFlash(new Color(0.2f, 1f, 0.4f), 0f, 0.5f, 0.4f, 0f);

                // 勝利判定
                if (TotalScore >= PointsToWin && !HasTriggeredGoalMeeting && !IsWaitingJudgementMeeting && !IsJudgementMeetingInProgress)
                {
                    HasTriggeredGoalMeeting = true;
                    IsWaitingJudgementMeeting = true;
                    MoreRolesInPolus.Scripts.Utils.CallMeetingHelper.CallMeetingPreferDeadBody(MyPlayer, MeetingHud.Instance != null);
                }
            }
            else
            {
                bool shouldShowScoreTitleShower = TotalScore < PointsToWin && !HasTriggeredGoalMeeting;

                // スコア表示（赤色）+ フラッシュ
                var currentGame = NebulaAPI.CurrentGame;
                if (shouldShowScoreTitleShower && currentGame != null)
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
            ClearActivationAreaVisualizations();
        }

        /// <summary>
        /// 前回通知した閾値レベル（0=未通知、1=50%、2=75%、3=90%）
        /// </summary>
        private int LastNotifiedThreshold = 0;

        /// <summary>
        /// 規定スコア到達後、強制会議を開始待ち中かどうか
        /// </summary>
        private bool IsWaitingJudgementMeeting = false;

        /// <summary>
        /// 規定スコア到達後の審判会議中かどうか
        /// </summary>
        private bool IsJudgementMeetingInProgress = false;

        /// <summary>
        /// 規定スコア到達による強制会議を既に発火したかどうか
        /// </summary>
        private bool HasTriggeredGoalMeeting = false;

        /// <summary>
        /// 会議開始時に画面を閉じ、閾値を超えた場合スコアを全員に共有
        /// </summary>
        [Local]
        void OnMeetingStart(MeetingStartEvent ev)
        {
            CloseAllScreens();

            if (!AmOwner) return;

            if (IsWaitingJudgementMeeting)
            {
                IsWaitingJudgementMeeting = false;
                IsJudgementMeetingInProgress = true;
            }

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
        /// 会議終了時、審判会議で生き残っていればCoordinator勝利、吊られていれば続行
        /// </summary>
        [Local]
        void OnMeetingEnd(MeetingEndEvent ev)
        {
            if (!AmOwner) return;
            if (!IsJudgementMeetingInProgress) return;

            IsJudgementMeetingInProgress = false;

            // 会議終了直後は状態更新の順序差があるため、1フレーム遅らせて判定する
            NebulaManager.Instance.ScheduleDelayAction(() =>
            {
                if (!AmOwner) return;
                if (MyPlayer.IsDead) return; // 吊られた場合はそのまま続行

                var winners = BitMasks.AsPlayer();
                winners.Add(MyPlayer);
                NebulaAPI.CurrentGame?.RequestGameEnd(CoordinatorTeamInfo.End, winners);
            });
        }

        /// <summary>
        /// 会議終了後（タスクフェーズ再開時）にクールダウンをリセット
        /// </summary>
        [Local]
        void OnTaskPhaseRestart(TaskPhaseRestartEvent ev)
        {
            if (AmOwner)
            {
                // フェーズ開始時はTitleShower通知を出さない
                RerollCurrentActivationAreas(false);
            }

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
