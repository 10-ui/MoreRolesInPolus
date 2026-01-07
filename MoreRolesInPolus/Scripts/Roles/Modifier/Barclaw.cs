using System;
using System.Collections.Generic;
using System.Text;

namespace MoreRolesInPolus.Roles.Modifier;

public class Barclaw : DefinedAllocatableModifierTemplate, DefinedAllocatableModifier
{


    private Barclaw() : base("barclaw", "barclaw", new(180, 0, 0))
    {

    }

    static public Barclaw MyRole = new Barclaw();
    RuntimeModifier RuntimeAssignableGenerator<RuntimeModifier>.CreateInstance(GamePlayer player, int[] arguments) => new Instance(player);
    public class Instance : RuntimeAssignableTemplate, RuntimeModifier
    {
        DefinedModifier RuntimeModifier.Modifier => MyRole;
        public Instance(GamePlayer player) : base(player)
        {
        }

        void RuntimeAssignable.OnActivated() { }

        Dictionary<Player, (DefinedRole role, int roleCount)> roleMap = [];


        int numOfDeadBody = 0;

        [Local]
        void OnDeadBodyGenerated(DeadBodyInstantiateEvent evt)
        {
            if (AmOwner)
            {
                numOfDeadBody++;

                NebulaAPI.CurrentGame?.GetModule<TitleShower>()?.SetText(
                    numOfDeadBody.ToString(), // int を string に変換
                    new(100, 100, 100),
                    5.5f,
                    true
                );
            }
        }


        void OnPlayerRevived(PlayerReviveEvent ev)
        {

        }

    }
}
