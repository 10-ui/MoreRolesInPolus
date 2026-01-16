using Nebula.Roles.Abilities;
using Nebula.Roles.Scripts;
using System;
using System.Collections.Generic;
using Virial;
using Virial.Assignable;
using Virial.Components;
using Virial.Configuration;
using Virial.Events.Game;
using Virial.Events.Player;
using Virial.Game;
using Virial.Helpers;
using Virial.Runtime;

namespace MoreRolesInPolus.Roles.Neutral;

[NebulaPreprocess(PreprocessPhase.BuildAssignmentTypes)]
internal class DeadRunnerTeamInfo
{
    static public RoleTeam? MyTeam { get; private set; }
    static public GameEnd? End { get; private set; }
    static public Virial.Color TeamColor { get; private set; }

    static private void Preprocess(NebulaPreprocessor preprocessor)
    {
        TeamColor = new(120, 60, 180);
        MyTeam = preprocessor.CreateTeam("teams.deadrunner", TeamColor, TeamRevealType.OnlyMe);
        End = preprocessor.CreateEnd("deadrunner", TeamColor);
    }
}

// DeadRunner（死体運搬者）
internal class DeadRunner : DefinedRoleTemplate, DefinedRole
{

    // 設定オプション
    static private FloatConfiguration RequiredDistanceOption = NebulaAPI.Configurations.Configuration(
        "options.role.deadrunner.requiredDistance",
        (50f, 200f, 10f),
        100f,
        FloatConfigurationDecorator.Ratio
    );
    static private FloatConfiguration SpeedBoostOption = NebulaAPI.Configurations.Configuration(
        "options.role.deadrunner.speedBoost",
        (1.25f, 3f, 0.125f),
        2f,
        FloatConfigurationDecorator.Ratio
    );
    static private FloatConfiguration VisionBoostOption = NebulaAPI.Configurations.Configuration(
        "options.role.deadrunner.visionBoost",
        (1f, 2f, 0.125f),
        1.25f,
        FloatConfigurationDecorator.Ratio
    );
    static public DeadRunner MyRole = new DeadRunner();
    // 統計
    private static GameStatsEntry StatsDistanceCarried = NebulaAPI.CreateStatsEntry(
        "stats.deadrunner.distanceCarried",
        GameStatsCategory.Roles,
        MyRole
    );

    private DeadRunner() : base(
        "deadrunner",
        DeadRunnerTeamInfo.TeamColor,
        RoleCategory.NeutralRole,
        DeadRunnerTeamInfo.MyTeam,
        [RequiredDistanceOption, SpeedBoostOption, VisionBoostOption]
    )
    {
    }

    RuntimeRole RuntimeAssignableGenerator<RuntimeRole>.CreateInstance(GamePlayer player, int[] arguments)
        => new Instance(player, arguments);

    // 明示的インターフェイス実装を通常の static プロパティに変更
    public static bool HasTips => false;
    public static bool HasAbility => false;
    public static bool HasWinCondition => true;



    public class Instance : RuntimeAssignableTemplate, RuntimeRole
    {
        DefinedRole RuntimeRole.Role => MyRole;
        private bool wasHoldingDeadBody = false;
        public Instance(GamePlayer player, int[] arguments) : base(player)
        {
        }

        public void OnActivated()
        {

            new Draggable(MyPlayer).Register(new FunctionalLifespan(() => !IsDeadObject));
            if (AmOwner)
            {

                GameOperatorManager.Instance?.Subscribe<GameUpdateEvent>(ev =>
                {
                    bool isHolding = MyPlayer.HoldingAnyDeadBody && !MyPlayer.IsDead;

                    // 死体を持ち始めた時
                    if (isHolding && !wasHoldingDeadBody)
                    {
                        using (RPCRouter.CreateSection("DeadRunnerBoost"))
                        {

                            MyPlayer.GainSpeedAttribute( SpeedBoostOption, 10000f, false, 100, "DeadRunnerSpeed");
                            MyPlayer.GainAttribute(PlayerAttributes.Invisible, 10000f, false, 100, "DeadRunnerInvisible");
                        }
                    }
                    // 死体を手放した時
                    else if (!isHolding && wasHoldingDeadBody)
                    {
                        using (RPCRouter.CreateSection("DeadRunnerBoostEnd"))
                        {
                            MyPlayer.RemoveAttributeByTag("DeadRunnerSpeed");
                            MyPlayer.RemoveAttributeByTag("DeadRunnerInvisible");
                        }
                    }

                    wasHoldingDeadBody = isHolding;
                }, this);

                // 死体への矢印を表示（Vultureのコードから）
                var ability = new DeadbodyArrowAbility().Register(this);
                GameOperatorManager.Instance?.Subscribe<GameUpdateEvent>(
                    ev => ability.ShowArrow = !MyPlayer.IsDead,
                    this
                );
            }




        }

        bool RuntimeRole.HasImpostorVision => true;
    }
}