using Nebula.Modules;
using Virial;
using Virial.Events.Game.Meeting;
using Virial.Game;

namespace MoreRolesInPolus.Helpers;

public class CoordinatorHelpers
{
    [NebulaRPCHolder]
    public class RpcHolder
    {
        public static readonly RemoteProcess<byte> RpcNotifyTarget = new RemoteProcess<byte>("Coordinator.NotifyTarget", (targetId, _) =>
        {
            if (PlayerControl.LocalPlayer.PlayerId == targetId)
            {
                // Play sound
                NebulaAsset.PlaySE(NebulaAudioClip.ButtonBreaking, false, 1.0f);
                
                // Show HUD message
                NebulaAPI.CurrentGame?.GetModule<TitleShower>()?.SetText(
                    Language.Translate("coordinator.notification.failed"), 
                    UnityEngine.Color.red, 
                    5.0f, 
                    true
                );
                
                // Optional: Flash screen
                AmongUsUtil.PlayFlash(UnityEngine.Color.red);
            }
        });
    }
}
