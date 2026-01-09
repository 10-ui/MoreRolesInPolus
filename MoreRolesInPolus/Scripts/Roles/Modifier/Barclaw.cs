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

    static private readonly IntegerConfiguration NumOfDeadRequired = NebulaAPI.Configurations.Configuration("options.role.barclaw.numOfDeadRequired", (1, 5), 2);
    static private readonly BoolConfiguration ShowMeetingInFlash = NebulaAPI.Configurations.Configuration("options.role.bait.showKillFlash", false);
    static private readonly FloatConfiguration MeetingDelayOption = NebulaAPI.Configurations.Configuration("options.role.bait.reportDelay", (0f, 5f, 0.5f), 0f, FloatConfigurationDecorator.Second);
    static private readonly FloatConfiguration MeetingDelayDispersionOption = NebulaAPI.Configurations.Configuration("options.role.bait.reportDelayDispersion", (0f, 10f, 0.25f), 0.5f, FloatConfigurationDecorator.Second);


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

                if (numOfKillCount >= NumOfDeadRequired && !myDead)
                {
                    AmongUsUtil.PlayQuickFlash(MyRole.UnityColor);
                    CallMeeting(MyPlayer);
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


        void OnMeetingEnd(MeetingEndEvent ev)
        {
            if (AmOwner)
            {
                nowMeeting = false;
                numOfKillCount = 0;
                deadPlayer.Clear();
            }

        }


        void OnMeetingPreStart(MeetingPreStartEvent ev)
        {
            if (AmOwner)
            {
                nowMeeting = true;
                numOfKillCount = 0;
                deadPlayer.Clear();
            }

        }

        [NebulaRPC]
        public static void CallMeeting(GamePlayer p)
        {
            if (AmongUsClient.Instance.AmHost)
            {
                var player = PlayerControl.AllPlayerControls.GetFastEnumerator().FirstOrDefault(c => c.PlayerId == p.PlayerId);
                MeetingRoomManager.Instance.AssignSelf(player, null);
                if (GameManager.Instance.CheckTaskCompletion())
                {
                    return;
                }
                DestroyableSingleton<HudManager>.Instance.OpenMeetingRoom(player);
                player.RpcStartMeeting(null);
            }
        }

    }
}
