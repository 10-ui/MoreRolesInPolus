using MoreRolesInPolus.Scripts.Roles.script;
using System;
using System.Collections.Generic;
using System.Text;
using static Rewired.UI.ControlMapper.ControlMapper;

namespace MoreRolesInPolus.Roles.Modifier;

public class Barclaw : DefinedAllocatableModifierTemplate, DefinedAllocatableModifier
{


    private Barclaw() : base("barclaw", "barclaw", new(118, 118, 118), [numOfDeadRequired, numOfMaxButton, enableAfterSecondTurn, showMeetingInFlash, meetingDelayOption, meetingDelayDispersionOption, AllowGhostMeeting])
    {

    }
    static private readonly IntegerConfiguration numOfDeadRequired = NebulaAPI.Configurations.Configuration("options.role.barclaw.numOfDeadRequired", (1, 10), 2);
    static private readonly IntegerConfiguration numOfMaxButton = NebulaAPI.Configurations.Configuration("options.role.barclaw.numOfMaxButton", (1, 10), 2);
    static private readonly BoolConfiguration enableAfterSecondTurn = NebulaAPI.Configurations.Configuration("options.role.barclaw.enableAfterSecondTurn", false);
    static private readonly BoolConfiguration showMeetingInFlash = NebulaAPI.Configurations.Configuration("options.role.barclaw.showMeetingInFlash", false);
    static private readonly FloatConfiguration meetingDelayOption = NebulaAPI.Configurations.Configuration("options.role.barclaw.meetingDelayOption", (0f, 5f, 0.5f), 2f, FloatConfigurationDecorator.Second);
    static private readonly FloatConfiguration meetingDelayDispersionOption = NebulaAPI.Configurations.Configuration("options.role.barclaw.meetingDelayDispersionOption", (0f, 10f, 0.25f), 3f, FloatConfigurationDecorator.Second);
    //死んでいても緊急会議がが発動するか
    static private readonly BoolConfiguration AllowGhostMeeting = NebulaAPI.Configurations.Configuration("options.role.barclaw.allowGhostMeeting", false);
    static public Barclaw MyRole = new Barclaw();
    RuntimeModifier RuntimeAssignableGenerator<RuntimeModifier>.CreateInstance(GamePlayer player, int[] arguments) => new Instance(player);


    public class Instance : RuntimeAssignableTemplate, RuntimeModifier
    {
        DefinedModifier RuntimeModifier.Modifier => MyRole;

        bool RuntimeAssignable.CanBeAwareAssignment => NebulaGameManager.Instance?.CanSeeAllInfo ?? false;
        public Instance(GamePlayer player) : base(player)
        {
        }

        void RuntimeAssignable.OnActivated() { }

        void RuntimeAssignable.DecorateNameConstantly(ref string name, bool canSeeAllInfo, bool inEndScene)
        {
            if (canSeeAllInfo || inEndScene) name += MyRole.GetRoleIconTagSmall();
        }

        bool nowMeeting = false;
        bool myDead = false;
        int numOfKillCount = 0;
        int numOfButtonCount = 0;
        int nowTurn = 1;
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

                if (numOfKillCount >= numOfDeadRequired && !myDead && numOfButtonCount >= numOfMaxButton)
                {
                    if(enableAfterSecondTurn && nowTurn <= 2) return;
                    if(showMeetingInFlash) AmongUsUtil.PlayQuickFlash(MyRole.UnityColor);

                    float t = Mathn.Max(0.1f, meetingDelayOption) + meetingDelayDispersionOption * (float)System.Random.Shared.NextDouble();
                    

                    if(nowMeeting) return;
                    NebulaManager.Instance.StartCoroutine(WaitAndCallCoroutine(t).WrapToIl2Cpp());
                    numOfButtonCount ++;

                }
            }
        }
        // t秒待ってからミーティングを呼ぶコルーチン
        System.Collections.IEnumerator WaitAndCallCoroutine(float t)
        {
            yield return new UnityEngine.WaitForSeconds(t);

            // 待機後に条件がまだ満たされていれば実行
            if (!AmOwner) yield break;
            if (nowMeeting) yield break;
            if (myDead && !AllowGhostMeeting) yield break;

            CallMeetingHelper.CallMeeting(MyPlayer);
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
                nowTurn++;
                nowMeeting = true;
                numOfKillCount = 0;
                deadPlayer.Clear();
            }

        }

    }
}
