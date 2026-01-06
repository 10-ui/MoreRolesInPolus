using AsmResolver.PE.Imports.Builder;
using Nebula.Roles.Complex;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements.Experimental;
using Virial.Command;
using Virial.Runtime;
namespace MoreRolesInPolus.Roles.Neutral;

[NebulaPreprocess(PreprocessPhase.BuildAssignmentTypes)]
internal class AccuserTeamInfo
{
    static public RoleTeam? MyTeam { get; private set; }
    static public GameEnd? End { get; private set; }
    static public Virial.Color TeamColor { get; private set; }
    static private void Preprocess(NebulaPreprocessor preprocessor)
    {
        TeamColor = new(120, 60, 180);
        MyTeam = preprocessor.CreateTeam("teams.accuser", TeamColor, TeamRevealType.OnlyMe);
        End = preprocessor.CreateEnd("accuser", TeamColor);
    }
}

// Accuser（告発者）
// 推測を成功させて勝利を目指す第三陣営役職
// 会議中以外暇な役職になってしまってるから仕事を与えたい
// 今考えてるのとしては何かをしないとゲッサーの玉をゲットできないというもの
// 例えば生きてる人全員に一回触れる(クールタイムなどはなしただ触れるだけもしくはクリックするだけ)
internal class Accuser : DefinedRoleTemplate, DefinedRole
{

    private Accuser() : base("accuser", AccuserTeamInfo.TeamColor, RoleCategory.NeutralRole, AccuserTeamInfo.MyTeam, [NumOfGuessToWinOption, NumOfGuessPerMeetingOption, GetLongTaskHintOption, NumOfLongTaskOption, MaxAccuserHint, NumOfCandidatesOption])
    {
    }

    Image? DefinedAssignable.IconImage => researchSprite;
    static private readonly Image researchSprite = NebulaAPI.AddonAsset.GetResource(string.Format("Crewmate/Researcher/ResearchButton.png"))!.AsImage(115f)!;

    static private IntegerConfiguration NumOfGuessToWinOption = NebulaAPI.Configurations.Configuration("options.role.accuser.NumOfGuessToWinOption", (1, 10), 2);
    static private IntegerConfiguration NumOfGuessPerMeetingOption = NebulaAPI.Configurations.Configuration("options.role.accuser.numOfGuessPerMeeting", (1, 10), 1);
    static private BoolConfiguration GetLongTaskHintOption = NebulaAPI.Configurations.Configuration("options.role.accuser.GetLongTaskHintOption", true);
    static private IntegerConfiguration NumOfLongTaskOption = NebulaAPI.Configurations.Configuration("options.role.accuser.NumOfLongTaskOption", (1, 10), 3);
    static private IntegerConfiguration MaxAccuserHint = NebulaAPI.Configurations.Configuration("options.role.accuser.MaxAccuserHint", (1, 10), 2);
    static private IntegerConfiguration NumOfCandidatesOption = NebulaAPI.Configurations.Configuration("options.role.accuser.NumOfCandidatesOption", (1, 5), 3);
    static public Accuser MyRole = new Accuser();

    // 統計：推測した回数
    static private GameStatsEntry StatsGuess = NebulaAPI.CreateStatsEntry("stats.accuser.guess", GameStatsCategory.Roles, MyRole);

    RuntimeRole RuntimeAssignableGenerator<RuntimeRole>.CreateInstance(GamePlayer player, int[] arguments) => new Instance(player, arguments);

    public class Instance : RuntimeAssignableTemplate, RuntimeRole
    {
        DefinedRole RuntimeRole.Role => MyRole;
        // ゲーム全体で残っている推測回数
        private int leftGuess;
        // 勝利に必要な推測成功回数（初期値)
        private int totalGuesses;
        //hintを入手するためのlongタスク数
        int NumOfLongTask = NumOfLongTaskOption;
        //hintの最大回数
        int maxhint;
        //入手したヒントの回数
        private int leftHint = 0;
        //入手したヒント
        Dictionary<Player, (string longName,string shortName)> roleTextHint = new();
        // 推測ウィンドウの参照
        private MetaScreen? lastGuesserWindow = null;

        public Instance(GamePlayer player, int[] arguments) : base(player)
        {
            totalGuesses = arguments.Length >= 1 ? arguments[0] : NumOfGuessToWinOption;
            leftGuess = totalGuesses;
        }

        public Instance(GamePlayer myPlayer) : base(myPlayer)
        {
            totalGuesses = NumOfGuessToWinOption;
            leftGuess = totalGuesses;
        }

        // 
        int[]? RuntimeAssignable.RoleArguments => new int[] { totalGuesses };

        // 会議開始時：ゲッサーの能力を付与
        [Local]
        void OnMeetingStart(MeetingStartEvent ev)
        {
            // この会議で残っている推測回数
            int leftGuessPerMeeting = NumOfGuessPerMeetingOption;

            // 各プレイヤーに推測ボタンを追加
            NebulaAPI.CurrentGame?.GetModule<MeetingPlayerButtonManager>()?.RegisterMeetingAction(
                new(MeetingPlayerButtonManager.Icons.AsLoader(0),
                state => {
                    var p = state.MyPlayer;
                    // 推測ウィンドウを開く
                    lastGuesserWindow = OpenGuessWindow(leftGuessPerMeeting, leftGuess, (r) =>
                    {

                        if (PlayerControl.LocalPlayer.Data.IsDead) return;
                        if (!(MeetingHud.Instance.state == MeetingHud.VoteStates.Voted || MeetingHud.Instance.state == MeetingHud.VoteStates.NotVoted)) return;
                        if (!MeetingHudExtension.CanUseAbilityFor(p, true)) return;

                        // 統計：推測回数を記録
                        StatsGuess.Progress();
                        // 推測が正しいかチェック
                        bool isCorrect = p.Role.CheckGuessAbility(r);

                        if (isCorrect)
                        {
                            // 正解：対象プレイヤーを殺害
                            NebulaAPI.CurrentGame?.LocalPlayer.MurderPlayer(p, PlayerState.Guessed, EventDetail.Guess, KillParameter.MeetingKill, KillCondition.BothAlive);
                        }
                        else
                        {
                            // 不正解：自分が死亡
                            NebulaAPI.CurrentGame?.LocalPlayer.MurderPlayer(NebulaAPI.CurrentGame.LocalPlayer, PlayerState.Misguessed, EventDetail.Missed, KillParameter.MeetingKill, KillCondition.BothAlive);
                        }

                        // 推測回数を減らす
                        leftGuess--;
                        leftGuessPerMeeting--;

                        // ウィンドウを閉じる
                        if (lastGuesserWindow) lastGuesserWindow.CloseScreen();
                        lastGuesserWindow = null;
                    });
                },
                // ボタンを表示する条件
                p => !p.MyPlayer.IsDead && !p.MyPlayer.AmOwner && leftGuess > 0 && leftGuessPerMeeting > 0 && !PlayerControl.LocalPlayer.Data.IsDead && GameOperatorManager.Instance!.Run(new PlayerCanGuessPlayerLocalEvent(NebulaAPI.CurrentGame!.LocalPlayer, p.MyPlayer, true)).CanGuess
            ));
        }

        // 推測ウィンドウを開く
        private MetaScreen OpenGuessWindow(int leftGuessPerMeeting, int leftGuess, Action<DefinedRole> onSelected)
        {
            // 会議ごとの制限がある場合は "1 (3)" のように表示
            string leftStr = leftGuessPerMeeting < leftGuess
                ? $"{leftGuessPerMeeting} ({leftGuess})"
                : leftGuess.ToString();

            // 役職選択ウィンドウを開く
            return MeetingRoleSelectWindow.OpenRoleSelectWindow(null, r => r.CanBeGuess, GamePlayer.LocalPlayer?.FeelBeTrueCrewmate ?? false, Language.Translate("role.guesser.leftGuess") + " : " + leftStr, onSelected);
        }

        // 自分が死亡した時：ウィンドウを閉じる
        [Local, OnlyMyPlayer]
        void OnDead(PlayerDieEvent ev)
        {
            if (lastGuesserWindow) lastGuesserWindow.CloseScreen();
            lastGuesserWindow = null;
        }

        // プレイヤーが殺害された時：勝利条件をチェック
        [Local, OnlyMyPlayer]
        void OnGuessPlayer(PlayerKillPlayerEvent ev)
        {
            // 推測成功による殺害の場合
            if (ev.Dead.PlayerState == PlayerStates.Guessed)
            {
                // 必要な推測成功回数に達したら勝利
                if (leftGuess <= 0)
                {
                    var bitmask = BitMasks.AsPlayer();
                    bitmask.Add(MyPlayer);

                    NebulaAPI.CurrentGame.RequestGameEnd(AccuserTeamInfo.End, bitmask);
                }
            }
        }

        //役職のヒントを入手するがlongかshortか選択をもっと楽にしたい
        private List<String> GetRoleHints(GamePlayer targetPlayer, String letter)
        {
            if (letter == "long")
            {


                List<String> hints = new();

                HashSet<DefinedRole> possibleRoles = new();

                hints.Add(targetPlayer.Role.DisplayColoredName);

                foreach (var r in NebulaAPI.
                {
                    if (r.IsSpawnble && r != targetPlayer.Role.Role) possibleRoles.Add(r);
                }

                // HashSet にはインデクサがないため、リストに変換してからインデックスアクセスする
                var possibleRolesList = possibleRoles.ToList();
                possibleRolesList.MyShuffle();

                for (int i = 0; i < NumOfCandidatesOption - 1; i++)
                {
                    hints.Add(possibleRolesList[i].DisplayColoredName);
                }

                hints.MyShuffle();
                return hints;
            }
            else {                 
                List<String> hints = new();
                HashSet<DefinedRole> possibleRoles = new();
                hints.Add(targetPlayer.Role.DisplayColoredShort);
                foreach (var r in NebulaAPI.
                {
                    if (r.IsSpawnble && r != targetPlayer.Role.Role) possibleRoles.Add(r);
                }
                // HashSet にはインデクサがないため、リストに変換してからインデックスアクセスする
                var possibleRolesList = possibleRoles.ToList();
                possibleRolesList.MyShuffle();
                for (int i = 0; i < NumOfCandidatesOption - 1; i++)
                {
                    hints.Add(possibleRolesList[i].DisplayColoredShort);
                }
                hints.MyShuffle();
                return hints;
            }
        }


        void setAccuserTask()
        {
            if (AmOwner && GetLongTaskHintOption)
            {

                using (RPCRouter.CreateSection("AccuserTask"))
                {

                    MyPlayer.Tasks.Unbox().ReplaceTasksAndRecompute(0, NumOfLongTask, 0);
                    MyPlayer.Tasks.Unbox().BecomeToOutsider();
                    

                }
            }

        }

        RoleTaskType RuntimeRole.TaskType => RoleTaskType.RoleTask;

        //役職のヒントを取得する

        public void OnActivated()
        {
        }
        public void OnGameStart(GameStartEvent ev)
        {
            if (GetLongTaskHintOption) setAccuserTask();
        }

        [OnlyMyPlayer]
        public void OnTaskCompleted(PlayerTaskCompleteLocalEvent ev)
        {
            if (GetLongTaskHintOption && MyPlayer.Tasks.CurrentCompleted >= NumOfLongTaskOption)
            {
                //NebulaAPI.CurrentGame?.GetModule<TitleShower>()?.SetText("finish : " + , new(100, 100, 100), 5.5f, true);

                leftHint++;
                maxhint++;
                GetHintButton();

                if(maxhint <= MaxAccuserHint - 1)
                {
                    MyPlayer.Tasks.Unbox().ReplaceTasksAndRecompute(0, NumOfLongTask, 0);
                }
            }
        }

        private void GetHintButton()
        {
            if (leftHint > 0)
            {
                var playerTracker = NebulaAPI.Modules.PlayerTracker(this, MyPlayer);
                var hintButton = NebulaAPI.Modules.EffectButton(this, MyPlayer, VirtualKeyInput.Ability,
                        1f, 0f, "accuser_hint", researchSprite,
                        _ => playerTracker.CurrentTarget != null, _ => leftHint > 0);
                hintButton.ShowUsesIcon(3, leftHint.ToString());
                hintButton.OnClick = (Button) =>
                {
                    var target = playerTracker.CurrentTarget;
                    String targetRoleName = playerTracker.CurrentTarget.Role.DisplayColoredName;
                    List<string> roleHintsLong = GetRoleHints(target,"long");
                    List<string> roleHintsShort = GetRoleHints(target, "short");
                    string hintTextLong = string.Join(" ", roleHintsLong);
                    string hintTextShort = string.Join(" ", roleHintsShort);
                    roleTextHint[target] = (hintTextLong, hintTextShort);
                    NebulaAPI.CurrentGame?.GetModule<TitleShower>()?.SetText(hintTextLong, new(100, 100, 100), 5.5f, true);
                    leftHint--;
                };
            }
        }

        void ReflectRoleHintName(PlayerSetFakeRoleNameEvent ev)
        {

            if (roleTextHint.ContainsKey(ev.Player) && GetLongTaskHintOption)
            {
                string hintText = roleTextHint[ev.Player].shortName;
                ev.Alternate(hintText);
            }
        }
    }
}