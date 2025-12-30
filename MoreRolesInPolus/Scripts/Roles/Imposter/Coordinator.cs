
namespace MoreRolesInPolus.Roles.Imposter;

//シェイプシフターの画面で人を選んでその後マップを選択、あってればキル。間違えてもなにもなし。
//レポートした人にだけ距離（5K！）を通知する。
//
public class Coodinator : DefinedSingleAbilityRoleTemplate<Coodinator.Ability>, DefinedRole
{
    private Coodinator() : base("coodinator", new(Palette.ImpostorRed), RoleCategory.ImpostorRole, NebulaTeams.ImpostorTeam)
    {
        ConfigurationHolder?.AddTags(ConfigurationTags.TagFunny);
    }

    static public readonly Coodinator MyRole = new();

    public override Ability CreateAbility(GamePlayer player, int[] arguments) => new(player, arguments.GetAsBool(0));

    public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
    {
        public Ability(GamePlayer player, bool isUsurped) : base(player, isUsurped)
        {

        }
    }
}