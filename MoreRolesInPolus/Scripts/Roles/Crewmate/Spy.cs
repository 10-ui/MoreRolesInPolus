namespace MoreRolesInPolus.Roles.Crewmate;

//TORのSpy
//
public class Spy : DefinedSingleAbilityRoleTemplate<Spy.Ability>, DefinedRole, HasCitation
{
    private Spy() : base("spy", new(Palette.ImpostorRed), RoleCategory.ImpostorRole, NebulaTeams.ImpostorTeam)
    {
        ConfigurationHolder?.AddTags(ConfigurationTags.TagFunny);
    }
    Citation? HasCitation.Citation => Citations.TheOtherRoles;

    static public readonly Spy MyRole = new();

    public override Ability CreateAbility(GamePlayer player, int[] arguments) => new(player, arguments.GetAsBool(0));

    public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
    {
        public Ability(GamePlayer player, bool isUsurped) : base(player, isUsurped)
        {

        }
    }
}