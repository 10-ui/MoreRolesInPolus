
namespace Scripts.Roles.Impostor;


public class Spoiler : DefinedSingleAbilityRoleTemplate<Spoiler.Ability>, DefinedRole
{
    private const string overlayKey = "role.spoiler.overlay";


    private Spoiler() : base("spoiler", NebulaTeams.ImpostorTeam.Color, RoleCategory.ImpostorRole, NebulaTeams.ImpostorTeam, [])
    {
    }

    static public readonly Spoiler MyRole = new();

    AbilityAssignmentStatus DefinedRole.AssignmentStatus => AbilityAssignmentStatus.KillersSide;
    MultipleAssignmentType DefinedRole.MultipleAssignment => MultipleAssignmentType.Allowed;


    public override Ability CreateAbility(GamePlayer player, int[] arguments) => new Ability(player, arguments.Length > 0 ? arguments[0] == 1 : false);

    public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
    {

        public Ability(Virial.Game.Player player, bool isUsurped) : base(player, isUsurped)
        {

        }

        Dictionary<Player, (DefinedRole role, int roleCount)> roleMap = [];



        int killCount = 0;

        [OnlyMyPlayer]
        void OnKillPlayer(PlayerKillPlayerEvent ev)
        { 
            if(AmOwner && ev.Player != ev.Dead)
            {
                var targetRole = ev.Dead.Role.ExternalRecognitionRole;
                int roleCount = Player.AllPlayers.Count(p => p.IsAlive && p.Role.ExternalRecognitionRole == targetRole);
                roleMap[ev.Dead] = (targetRole, roleCount);

                killCount++;

                int nowCount = killCount;

                var lifespan = FunctionalLifespan.GetTimeLifespan(7f);

                bool Isalive()
                {
                    return nowCount == killCount && lifespan.IsAliveObject;
                }

                NebulaAPI.GUI.ShowStickerOverlay(NebulaAPI.GUI.RawText(GUIAlignment.Center, AttributeAsset.OverlayContent, targetRole.DisplayColoredName + NebulaAPI.Language.Translate(overlayKey).Replace("%COUNT%", roleCount.ToString())), ev.Player.Position, () => !Isalive(),Isalive);//表示するたびにカウンター増やす　これが何番目かは覚える　


            }
        }

        void ReflectRoleName(PlayerSetFakeRoleNameEvent ev)
        {

            if (roleMap.ContainsKey(ev.Player))
            {
                var targetRole = roleMap[ev.Player];
                ev.Alternate(targetRole.role.DisplayColoredName + NebulaAPI.Language.Translate(overlayKey).Replace("%COUNT%", targetRole.roleCount.ToString()));
            } 
        }
    }







}