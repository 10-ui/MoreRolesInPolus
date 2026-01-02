using System.Reflection;

namespace MoreRolesInPolus.Roles.Crewmate;

public class Researcher : DefinedSingleAbilityRoleTemplate<Researcher.Ability>, DefinedRole
{
    private Researcher() : base("researcher", new(104,251,194), RoleCategory.CrewmateRole, NebulaTeams.CrewmateTeam, [SurveyCooldownOption, SurveyDurationOption, SurveyTimeOption, MaxSurveyOption])
    {
    }
    Image? DefinedAssignable.IconImage => iconImage;
    static readonly Image iconImage = NebulaAPI.AddonAsset.GetResource(string.Format("Crewmate/Researcher/Researcher.png"))!.AsImage()!;


    static private readonly FloatConfiguration SurveyCooldownOption = NebulaAPI.Configurations.Configuration("options.role.researcher.surveyCooldown", (0f, 60f, 2.5f), 20f, FloatConfigurationDecorator.Second);
    private static readonly FloatConfiguration SurveyDurationOption = NebulaAPI.Configurations.Configuration("options.role.researcher.surveyDuration", (1f, 10f, 0.5f), 3f, FloatConfigurationDecorator.Second);
    static private readonly FloatConfiguration SurveyTimeOption = NebulaAPI.Configurations.Configuration("options.role.researcher.surveytime", (10f, 60f, 2.5f), 30f, FloatConfigurationDecorator.Second);
    static private readonly IntegerConfiguration MaxSurveyOption = NebulaAPI.Configurations.Configuration("options.role.researcher.maxsurvey", (0, 10, 1), 5, null, num => num == 0 ? Language.Translate("options.noLimit") : num.ToString());


    static public readonly Researcher MyRole = new();


    public override Ability CreateAbility(GamePlayer player, int[] arguments) => new(player, arguments.GetAsBool(0), arguments.Get(1, -1));

    public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
    {
        private record ActionHistory(float Time, GamePlayer Player, string Text);
        private List<ActionHistory> allActions = [];

        private Dictionary<byte, string> lastPlayerRooms = new();
        private float timer = 0f;
        private const float interval = 0.5f;

        int leftUses;
        private record SurveyHistory(float Time, GamePlayer Player, string Content);
        private List<SurveyHistory> InthisturnResult = [];

        static private readonly Image researchSprite = NebulaAPI.AddonAsset.GetResource(string.Format("Crewmate/Researcher/ResearchButton.png"))!.AsImage(115f)!;


        string GetHistory(GamePlayer player, float from, float to)
        {
            StringBuilder result = new();

            foreach (var action in allActions)
            {
                if(action.Player != player)
                {
                    continue;
                };

                if(action.Time <= from){
                    continue;
                }
                 
                if (action.Time >= to) {
                    break;
                }
                float elapsedTime = MathF.Floor(to - action.Time);
                result.AppendLine($"{elapsedTime}秒前に" + action.Text);
            }
            return result.ToString();
        }

        public Ability(GamePlayer player, bool isUsurped, int leftUses) : base(player, isUsurped)
        {
            this.leftUses = leftUses;
            this.leftUses = MaxSurveyOption == 0 ? 1000 : MaxSurveyOption;

            if (AmOwner) 
            {
                //実行対象をきめるやつ
                var surveyTracker = ObjectTrackers.ForPlayerlike(this, null, MyPlayer, (p) => ObjectTrackers.PlayerlikeStandardPredicate(p));

                //発火ボタン
                var surveyButton = NebulaAPI.Modules.EffectButton(this, MyPlayer, VirtualKeyInput.Ability,
                        SurveyCooldownOption, SurveyDurationOption, "survey", researchSprite,
                        _ => surveyTracker.CurrentTarget != null, _ => this.leftUses > 0);
                if (this.leftUses < 20) surveyButton.ShowUsesIcon(4, this.leftUses.ToString());

                //調査を実行する関数
                void examinePlayer() 
                {
                    var examineTime = Time.time;
                    var TargetPlayer = surveyTracker.CurrentTarget.RealPlayer;
                    string historyText = GetHistory(TargetPlayer, examineTime - SurveyTimeOption, examineTime);
                    InthisturnResult.Add(new(examineTime, TargetPlayer, historyText));
                    this.leftUses--;
                    surveyButton.UpdateUsesIcon(this.leftUses.ToString());
                }

                //近くにいないとだめだよ
                surveyButton.OnEffectStart = _ => surveyTracker.KeepAsLongAsPossible = true;
                surveyButton.OnEffectEnd = (button) =>
                {
                    surveyTracker.KeepAsLongAsPossible = false;
                    if (surveyTracker.CurrentTarget == null) return;
                    if (MeetingHud.Instance) return;

                    if (GameOperatorManager.Instance?.Run(new PlayerInteractPlayerLocalEvent(MyPlayer, surveyTracker.CurrentTarget, new(RealPlayerOnly: true))).IsCanceled ?? true) return;

                    if (!button.EffectTimer!.IsProgressing) examinePlayer();

                    surveyButton.StartCoolDown();
                };

                surveyButton.OnUpdate = (button) => {
                    if (!button.IsInEffect) return;
                    if (surveyTracker.CurrentTarget == null) button.InterruptEffect();
                };

                surveyButton.EffectTimer = NebulaAPI.Modules.Timer(this, SurveyDurationOption);
                surveyButton.SetLabel("survey");
                surveyButton.SetAsUsurpableButton(this);
            }



        }

        // どこの部屋に"入った"か
        [Local]
        public void OnUpdate(GameUpdateEvent ev)
        {
            // マップが完全に読み込まれているかチェック
            if (NebulaAPI.CurrentGame?.CurrentMap == null) return;

            timer += ev.DeltaTime;
            if (timer < interval) return;
            timer = 0f;

            foreach (var gplayer in GamePlayer.AllPlayers)
            {
                if (gplayer.IsDead || gplayer.IsBlown) continue;

                UnityEngine.Vector2 pos = gplayer.TruePosition;

                var map = NebulaAPI.CurrentGame.CurrentMap;
                var getRoomNameMethod = map.GetType().GetMethod("GetRoomName", 
                    new[] { typeof(UnityEngine.Vector2), typeof(bool), typeof(bool), typeof(bool) });
                var roomResult = (string?)getRoomNameMethod?.Invoke(map, new object[] { pos, false, false, false });

                byte id = gplayer.PlayerId;

                if (!lastPlayerRooms.ContainsKey(id))
                {
                    lastPlayerRooms[id] = "";
                }

                if (lastPlayerRooms[id] != roomResult)
                {
                    allActions.Add(new(Time.time, gplayer, $"{roomResult}に入ったようだ..."));
                    lastPlayerRooms[id] = roomResult;
                }
            }
        }

        //ベントに入った
        [Local]
        void OnEnterVent(PlayerVentEnterEvent ev)
        {
            allActions.Add(new(Time.time, ev.Player, "ベントに入ったようだ..."));
        }

        //アクションを起こした
        [Local]
        void OnDoGameAction(PlayerDoGameActionEvent ev) 
        {
            if(ev.ActionType.IsPlacementAction == true)
            {
                allActions.Add(new(Time.time, ev.Player, "何かを設置したようだ..."));
            } else if(ev.ActionType.IsPhysicalAction == true)
            {
                allActions.Add(new(Time.time, ev.Player, "状態を変化させたようだ..."));
            } else
            {
                allActions.Add(new(Time.time, ev.Player, "アクションを起こした！"));
            }
        }

        //ミーティングボタンが押されたときに実行する
        //TODO:対象者になにか行動があるときのみ表示。なにもなければ生成しても行動なしになるようにする
        //アイコンを書きたい
        [Local]
        void OnMeetingStart(MeetingStartEvent ev) 
        {
            foreach (var history in InthisturnResult)
            {
                var now = Time.time;
                float elapsedTime = MathF.Floor(now - history.Time);
                var textContent = history.Content == "" ? "特に異常は見られなかったようだ。" : history.Content;

                var playerinfo = NebulaAPI.GUI.VerticalHolder(Virial.Media.GUIAlignment.Left,
                NebulaAPI.GUI.RawText(Virial.Media.GUIAlignment.Left, NebulaAPI.GUI.GetAttribute(Virial.Text.AttributeAsset.OverlayTitle), textContent));
                float cachedX = 0f; 
                float cachedY = 0f;

                var Holder = NebulaAPI.GUI.VerticalHolder(GUIAlignment.Left,
                new NoSGUIText(GUIAlignment.Left, AttributeAsset.OverlayTitle, NebulaAPI.GUI.RawTextComponent(history.Player.PlayerName))
                {
                    PostBuilder = (textObj) =>
                    {
                        cachedX = textObj.rectTransform.sizeDelta.x;
                        cachedY = textObj.rectTransform.sizeDelta.y;
                    }
                },
                new NoSGameObjectGUIWrapper(GUIAlignment.Left, () => (null!, new(0f, -cachedY))),
                NebulaAPI.GUI.HorizontalHolder(GUIAlignment.Right,
                    NebulaAPI.GUI.HorizontalMargin(cachedX),
                    new NoSGUIText(GUIAlignment.Right, AttributeAsset.OverlayTitle, NebulaAPI.GUI.RawTextComponent($"{elapsedTime}秒前"))
                ),playerinfo
                );
                NebulaAPI.CurrentGame?.GetModule<MeetingOverlayHolder>()?.RegisterOverlay(Holder, MeetingOverlayHolder.IconsSprite[6], (MyRole as DefinedRole).Color);
            }

            InthisturnResult.Clear();
            allActions.Clear();
        }
        // Local 全員の行動を自分の環境にだけ
        // OnlyMyPlayer 自分の行動をみんなに
        // OnlyHost Host視点での情報をとりたいとき
    }
}