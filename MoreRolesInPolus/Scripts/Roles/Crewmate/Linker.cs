using Virial.Game;
using Virial.Text;
using Virial.Events.Game.Meeting;
using Virial.Events.Player;
using Virial.DI;
using System.Collections.Generic;

namespace MoreRolesInPolus.Roles.Crewmate;

[NebulaRPCHolder]
public static class LinkerRpc
{
    public static HashSet<byte> LinkedBy = new();
    public static bool InMeeting = false;

    public static readonly RemoteProcess<(byte linkerId, byte targetId)> RpcSyncLinkTarget = new("Linker.SyncTarget", (msg, _) =>
    {
        var linker = NebulaGameManager.Instance?.GetPlayer(msg.linkerId);
        var target = NebulaGameManager.Instance?.GetPlayer(msg.targetId);
        if (linker != null && linker.TryGetAbility<Linker.Ability>(out var ability))
        {
            ability.SyncTarget(target);
        }

        if (target != null && target.AmOwner)
        {
            LinkedBy.Add(msg.linkerId);
        }
        else
        {
            LinkedBy.Remove(msg.linkerId);
        }
    });
}

/// <summary>
/// Linkerロールの情報を定義するクラスです。
/// </summary>
public class Linker : DefinedSingleAbilityRoleTemplate<Linker.Ability>, DefinedRole
{
    /// <summary>
    /// Linkerロール情報のコンストラクタです。ここで役職の内部名、色、割り当てのカテゴリ、所属陣営、およびオプションを設定します。
    /// </summary>
    public static GameActionType LinkAction;

    private Linker() : base("linker", new(159, 153, 137), RoleCategory.CrewmateRole, NebulaTeams.CrewmateTeam, [LinkCooldownOption, DoActionFlashOption, DoTaskFlashOption, DoKillFlashOption, DoKilledFlashOption, DoDoorOpenFlashOption, DoVentFlashOption, DoGameActionFlashOption, FollowUpSuicideOption])
    {
        LinkAction = new GameActionType("linker.link", this, isPlacementAction: false, isPhysicalAction: true, isCleanDeadBodyAction: false, isEquippingAction: false);
    }

    /// <summary>
    /// ロビーで変更できる設定を用意します。ゲーム中で編集できるように、すぐ上のコンストラクタで役職のオプションに追加します。
    /// </summary>
    static private readonly FloatConfiguration LinkCooldownOption = NebulaAPI.Configurations.Configuration("options.role.linker.linkCooldown", (0f, 30f, 2.5f), 10f, FloatConfigurationDecorator.Second);
    static public BoolConfiguration DoActionFlashOption = NebulaAPI.Configurations.Configuration("options.role.linker.doActionFlash", true);

    /// <summary>
    /// フラッシュを個別に設定する。DoActionFlashOptionがfalseの時のみ表示・編集可能。
    /// </summary>
    static public BoolConfiguration DoTaskFlashOption = NebulaAPI.Configurations.Configuration("options.role.linker.doTaskFlash", true, () => !DoActionFlashOption);
    static public BoolConfiguration DoKillFlashOption = NebulaAPI.Configurations.Configuration("options.role.linker.doKillFlash", true, () => !DoActionFlashOption);
    static public BoolConfiguration DoKilledFlashOption = NebulaAPI.Configurations.Configuration("options.role.linker.doKilledFlash", true, () => !DoActionFlashOption);
    static public BoolConfiguration DoDoorOpenFlashOption = NebulaAPI.Configurations.Configuration("options.role.linker.doDoorOpenFlash", true, () => !DoActionFlashOption);
    static public BoolConfiguration DoVentFlashOption = NebulaAPI.Configurations.Configuration("options.role.linker.doVentFlash", true, () => !DoActionFlashOption);
    static public BoolConfiguration DoGameActionFlashOption = NebulaAPI.Configurations.Configuration("options.role.linker.doGameActionFlash", true, () => !DoActionFlashOption);

    /// <summary>
    /// 後追い死設定。ONの場合、リンク先がキルされるとリンカーも死亡する。
    /// </summary>
    static public BoolConfiguration FollowUpSuicideOption = NebulaAPI.Configurations.Configuration("options.role.linker.followUpSuicide", false);

    /// <summary>
    /// 役職の情報を用意します。
    /// </summary>
    static public readonly Linker MyRole = new();

    /// <summary>
    /// 役職を割り当てるとき、プレイヤーに割り当てる能力を作成します。
    /// </summary>
    /// <param name="player">割り当てる対象のプレイヤー</param>
    /// <param name="arguments">役職の引数(役職の状態を引き継ぐために使用します。)</param>
    /// <returns></returns>
    public override Ability CreateAbility(GamePlayer player, int[] arguments) => new Ability(player, arguments.GetAsBool(0));

    /// <summary>
    /// フラッシュが有効かどうかを判定するヘルパーメソッドです。
    /// DoActionFlashOptionがtrueなら全フラッシュ有効、falseなら個別設定を参照します。
    /// </summary>
    /// <param name="individualOption">個別のフラッシュ設定</param>
    /// <returns>フラッシュを発動すべきならtrue</returns>
    static public bool IsFlashEnabled(BoolConfiguration individualOption)
    {
        if (DoActionFlashOption) return true;
        return individualOption;
    }

    /// <summary>
    /// 役職の能力を記述するクラスです。
    /// </summary>
    public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
    {
        private GamePlayer? LinkTarget = null;
        private PoolablePlayer? LinkIcon = null;
        private HashSet<byte> LinkedPlayerIds = new();

        /// <summary>
        /// リンク設置ボタンの画像
        /// TODO: 専用画像を作成したら差し替える
        /// </summary>
        static private readonly Image ButtonSprite = NebulaAPI.AddonAsset.GetResource("Crewmate/Researcher/ResearchButton.png")!.AsImage(115f)!;

        /// <summary>
        /// 役職能力のコンストラクタ。
        /// </summary>
        /// <param name="player">割り当て対象のプレイヤー</param>
        /// <param name="isUsurped">能力が簒奪されている場合、true</param>
        public Ability(GamePlayer player, bool isUsurped) : base(player, isUsurped)
        {
            if (AmOwner)
            {
                /// <summary>
                /// 対象プレイヤーを追跡するトラッカー。リンク済みプレイヤーは候補から除外する。
                /// </summary>
                var PlayerTracker = NebulaAPI.Modules.PlayerTracker(this, MyPlayer,
                    p => !LinkedPlayerIds.Contains(p.PlayerId));

                /// <summary>
                /// リンク設置ボタン。
                /// ターゲットが存在し、かつ未リンク状態の時のみ使用可能。
                /// </summary>
                var LinkButton = NebulaAPI.Modules.AbilityButton(this, MyPlayer, Virial.Compat.VirtualKeyInput.Ability,
                    LinkCooldownOption, "link", ButtonSprite,
                    _ => PlayerTracker.CurrentTarget != null && LinkTarget == null);

                LinkButton.OnClick = (button) =>
                {
                    /// 現在のターゲットをリンク対象として確定
                    var target = PlayerTracker.CurrentTarget;
                    SyncTarget(target);
                    /// ボタン上に対象プレイヤーのアイコンを表示
                    LinkIcon = (LinkButton as ModAbilityButtonImpl)?.GeneratePlayerIcon(target);

                    /// 決定したリンク先をホストを含む全クライアントに同期
                    LinkerRpc.RpcSyncLinkTarget.Invoke((MyPlayer.PlayerId, target!.PlayerId));
                };
            }
        }

        public void SyncTarget(GamePlayer? target)
        {
            LinkTarget = target;
            if (target != null) LinkedPlayerIds.Add(target.PlayerId);
        }

        /// <summary>
        /// リンク対象がタスクを完了した時にフラッシュを発生させます。
        /// </summary>
        [Local]
        void OnTaskComplete(PlayerTaskCompleteEvent ev)
        {
            if (!AmOwner) return;
            if (LinkTarget == null) return;
            if (ev.Player.PlayerId != LinkTarget.PlayerId) return;
            if (!IsFlashEnabled(DoTaskFlashOption)) return;

            TriggerFlash();
        }

        /// <summary>
        /// リンク対象がベントに入った時にフラッシュを発生させます。
        /// </summary>
        [Local]
        void OnVentEnter(PlayerVentEnterEvent ev)
        {
            if (!AmOwner) return;
            if (LinkTarget == null) return;
            if (ev.Player.PlayerId != LinkTarget.PlayerId) return;
            if (!IsFlashEnabled(DoVentFlashOption)) return;

            TriggerFlash();
        }

        /// <summary>
        /// リンク対象がキルを実行した時にフラッシュを発生させます。
        /// Murdererがリンク対象であるかを確認します。
        /// </summary>
        [Local]
        void OnKillPlayer(PlayerKillPlayerEvent ev)
        {
            if (!AmOwner) return;
            if (LinkTarget == null) return;
            if (ev.Murderer.PlayerId != LinkTarget.PlayerId) return;
            if (!IsFlashEnabled(DoKillFlashOption)) return;

            TriggerFlash();
        }

        /// <summary>
        /// リンク対象がGameAction(罠の設置、死体食い、陣形変化など)を起こした時にフラッシュを発生させます。
        /// </summary>
        [Local]
        void OnDoGameAction(PlayerDoGameActionEvent ev)
        {
            if (!AmOwner) return;
            if (LinkTarget == null) return;
            if (ev.Player.PlayerId != LinkTarget.PlayerId) return;
            if (!IsFlashEnabled(DoGameActionFlashOption)) return;

            TriggerFlash();
        }

        /// <summary>
        /// リンク対象がキルされた時に専用のTitleShower演出を表示します。
        /// Avengerのスタイルを参考に、「Link Disconnected...」テキストを振動付きで表示します。
        /// </summary>
        [Local]
        void OnLinkTargetKilled(PlayerKillPlayerEvent ev)
        {
            if (!AmOwner) return;
            if (LinkTarget == null) return;
            if (ev.Dead.PlayerId != LinkTarget.PlayerId) return;

            /// Avenger色（141, 111, 131）でTitleShower表示
            var AvengerColor = new UnityEngine.Color(141f / 255f, 111f / 255f, 131f / 255f);
            NebulaAPI.CurrentGame?.GetModule<TitleShower>()?.SetText("Link Disconnected...", AvengerColor, 5.5f, true);

            if (!IsFlashEnabled(DoKilledFlashOption)) return;

            TriggerFlash();
        }

        /// <summary>
        /// リンク対象が死亡した時に、後追い死設定がONならリンカーも死亡します。
        /// ホスト上でのみ実行され、リンク先の死因に合わせた死に方をします。
        /// Loversの PlayerDieOrDisconnectEvent パターンを参考にしています。
        /// </summary>
        [OnlyHost]
        void OnFollowUpSuicide(PlayerDieOrDisconnectEvent ev)
        {
            if (!FollowUpSuicideOption) return;
            if (LinkTarget == null) return;
            if (ev.Player.PlayerId != LinkTarget.PlayerId) return;
            if (MyPlayer.IsDead) return;

            if (ev is PlayerExiledEvent) return; /// 追放はLocalハンドラ側で処理する

            /// リンク先の死因に関する詳細情報（Viperの溶けた死体など）を引き継ぐ
            MyPlayer.PlayerStateExtraInfo = ev.Player.PlayerStateExtraInfo;

            /// リンク先の死因と同じPlayerStateで後追い死する
            MyPlayer.Suicide(ev.Player.PlayerState, EventDetail.Kill, KillParameter.NormalKill);
        }

        /// <summary>
        /// リンク対象が追放された時に、後追い死設定がONなら追加追放扱いで後追い死します。
        /// Avengerと同じパターンで、Localハンドラから ModMarkAsExtraVictim を呼び出します。
        /// 追放アニメーションが全プレイヤーに表示されます。
        /// </summary>
        [Local]
        void OnLinkTargetExiled(PlayerExiledEvent ev)
        {
            if (!FollowUpSuicideOption) return;
            if (LinkTarget == null) return;
            if (ev.Player.PlayerId != LinkTarget.PlayerId) return;
            if (MyPlayer.IsDead) return;

            MyPlayer.VanillaPlayer.ModMarkAsExtraVictim(null, PlayerState.Suicide, PlayerState.Suicide);
        }

        /// <summary>
        /// リンク対象のアクションによるフラッシュを発火する共通メソッドです。
        /// 他のイベントハンドラからも呼び出せます。
        /// </summary>
        private void TriggerFlash()
        {
            AmongUsUtil.PlayQuickFlash(MyRole.UnityColor);
        }

        /// <summary>
        /// 会議終了時にリンク対象をリセットする。
        /// 全生存者がリンク済みの場合、リンク済みリストも全クリアする。
        /// </summary>
        [Local]
        void OnMeetingEnd(MeetingEndEvent ev)
        {
            LinkTarget = null;

            var AliveOthers = GamePlayer.AllPlayers
                .Where(p => !p.IsDead && p.PlayerId != MyPlayer.PlayerId);

            /// 全員リンク済み → リセットして再選択を可能にする
            if (AliveOthers.All(p => LinkedPlayerIds.Contains(p.PlayerId)))
            {
                LinkedPlayerIds.Clear();
            }
        }
    }
}

internal class LinkerTargetVisualizer : AbstractModule<IGameModeStandard>, IGameOperator
{
    public override void Initialize(IGameModeStandard gameMode)
    {
        GameOperatorManager.Instance?.Register(this);
        LinkerRpc.LinkedBy.Clear();
        LinkerRpc.InMeeting = false;
    }

    [Local]
    void OnPreMeetingStart(MeetingPreStartEvent ev) { LinkerRpc.InMeeting = true; }

    [Local]
    void OnMeetingEnd(MeetingPreSyncEvent ev) { LinkerRpc.InMeeting = false; }

    [Local]
    void OnDecorateName(PlayerDecorateNameEvent ev)
    {
        if (LinkerRpc.LinkedBy.Count > 0 && LinkerRpc.InMeeting && ev.Player.AmOwner)
        {
            ev.Name += " ♥".Color(Linker.MyRole.UnityColor);
        }
    }
}