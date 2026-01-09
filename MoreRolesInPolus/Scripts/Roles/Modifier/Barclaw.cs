using System;
using System.Collections.Generic;
using System.Text;
using static Rewired.UI.ControlMapper.ControlMapper;

namespace MoreRolesInPolus.Roles.Modifier;

public class Barclaw : DefinedAllocatableModifierTemplate, DefinedAllocatableModifier
{


    private Barclaw() : base("barclaw", "barclaw", new(180, 0, 0))
    {

    }

    static public Barclaw MyRole = new Barclaw();
    RuntimeModifier RuntimeAssignableGenerator<RuntimeModifier>.CreateInstance(GamePlayer player, int[] arguments) => new Instance(player);

    static private readonly IntegerConfiguration numOfDeadRequired = NebulaAPI.Configurations.Configuration("options.role.barclaw.numOfDeadRequired", (1, 5), 2);


    public class Instance : RuntimeAssignableTemplate, RuntimeModifier
    {
        DefinedModifier RuntimeModifier.Modifier => MyRole;
        public Instance(GamePlayer player) : base(player)
        {
        }

        void RuntimeAssignable.OnActivated() { }


        bool nowMeeting = false;
        bool myDead = false;
        int numOfKillCount = 0;
        List<GamePlayer> deadPlayer = new List<GamePlayer>();

        [Local]
        void OnKillPlayer(PlayerKillPlayerEvent ev)
        {

            if (AmOwner && !nowMeeting)
            {
                if (MyPlayer == ev.Dead)
                {
                    myDead = true;
                }

                numOfKillCount++;
                deadPlayer.Add(ev.Dead);

                NebulaAPI.CurrentGame?.GetModule<TitleShower>()?.SetText(
                    numOfKillCount.ToString() + ":" + ev.Dead.PlayerName, // int を string に変換
                    new(100, 100, 100),
                    5.5f,
                    true
                );

                if (numOfKillCount >= numOfDeadRequired && !myDead)
                {
                    AmongUsUtil.PlayQuickFlash(MyRole.UnityColor);
                    //緊急会議が始まる予定
                }
            }
        }

        [Local]
        void OnPlayerRevived(PlayerReviveEvent ev)
        {
            if (AmOwner)
            {
                if (deadPlayer.Contains(ev.Revived))
                {
                    if (myDead && ev.Revived == MyPlayer)
                    {
                        myDead = false;
                    }
                    numOfKillCount--;
                    deadPlayer.Remove(ev.Revived);
                    NebulaAPI.CurrentGame?.GetModule<TitleShower>()?.SetText(
                        numOfKillCount.ToString() + ":" + ev.Revived.PlayerName, // int を string に変換
                        new(100, 100, 100),
                        5.5f,
                        true
                    );
                }
            }
        }

        void OnMeetingStart(MeetingStartEvent ev)
        {
            if (AmOwner)
            {
                nowMeeting = true;
                numOfKillCount = 0;
                deadPlayer.Clear();
            }
        }
        void OnMeetingEnd(MeetingEndEvent ev)
        {
            if (AmOwner)
            {
                nowMeeting = false;
                numOfKillCount = 0;
                deadPlayer.Clear();
            }

        }
    }
}
