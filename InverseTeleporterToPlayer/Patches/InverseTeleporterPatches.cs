using System.Reflection;
using HarmonyLib;
using GameNetcodeStuff;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using Unity.Netcode;
using System.Collections;
using Object = UnityEngine.Object;

namespace InverseTeleporterToPlayer.Patches
{
    [HarmonyPatch]
    class InverseTeleporterToPlayerPatch
    {
        struct LocationData
        {
            public bool isInElevator;
            public bool isInHangarShipRoom;
            public bool isInsideFactory;
        }

        static readonly MethodInfo TeleportPlayerOutWithInverseTeleporter = typeof(ShipTeleporter).GetMethod("TeleportPlayerOutWithInverseTeleporter", BindingFlags.NonPublic | BindingFlags.Instance);

        static readonly Vector3 BrazilPosition = new Vector3(9.33f, 5.2f, 1021.0f);

        static HashSet<int> playersToBeTeleported = new HashSet<int>();

        static Dictionary<ulong, LocationData> dropableObjectDroppedData = new Dictionary<ulong, LocationData>();

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ShipTeleporter), "TeleportPlayerOutWithInverseTeleporter")]
        public static bool TeleportToPlayer(int playerObj, ref Vector3 teleportPos, ShipTeleporter __instance)
        {
            if (playersToBeTeleported.Contains(playerObj))
            {
                playersToBeTeleported.Remove(playerObj);
                return true;
            }

            int targetIndex = StartOfRound.Instance.mapScreen.targetTransformIndex;
            bool isTargetIndexValiable = targetIndex >= 0 && targetIndex < StartOfRound.Instance.mapScreen.radarTargets.Count;

            if (!isTargetIndexValiable)
            {
                Plugin.Instance.Log.LogWarning($"Invalid target index {targetIndex} for inverse teleporter target");
                return false;
            }

            TransformAndName target = StartOfRound.Instance.mapScreen.radarTargets[targetIndex];
            string targetName = target.name;
            Vector3 targetPosition = target.transform.position;

            LocationData? locationData = GetValidLocationData(target, targetName, ref targetPosition);
            if (!locationData.HasValue || Vector3.Distance(BrazilPosition, targetPosition) < 25.0f)
            {
                Plugin.Instance.Log.LogInfo("Brazil");
                BrazilTransmiterNetworkHandler brazilTransmiter = Object.FindObjectOfType<BrazilTransmiterNetworkHandler>();
                if (brazilTransmiter)
                    brazilTransmiter.BrazilTransmissionServerRPC(playerObj);
                locationData = new LocationData
                {
                    isInElevator = true,
                    isInHangarShipRoom = true,
                    isInsideFactory = true
                };
                targetPosition = BrazilPosition;
            }

            playersToBeTeleported.Add(playerObj);
            Plugin.Instance.Log.LogDebug($"Setting inverse teleporter target to {targetName} at position {targetPosition}");
            TeleportPlayerOutWithInverseTeleporter.Invoke(__instance, new object[] { playerObj, targetPosition });

            PlayerControllerB teleportedPlayer = StartOfRound.Instance.allPlayerScripts[playerObj];
            teleportedPlayer.isInElevator = locationData.Value.isInElevator;
            teleportedPlayer.isInHangarShipRoom = locationData.Value.isInHangarShipRoom;
            teleportedPlayer.isInsideFactory = locationData.Value.isInsideFactory;

            return false;
        }

        static LocationData? GetValidLocationData(TransformAndName target, string targetName, ref Vector3 targetPosition)
        {
            LocationData locationData;
            if (target.isNonPlayer)
            {
                GrabbableObject droppedObject = target.transform.GetComponent<GrabbableObject>();
                if (droppedObject.playerHeldBy)
                {
                    locationData = new LocationData
                    {
                        isInElevator = droppedObject.playerHeldBy.isInElevator,
                        isInHangarShipRoom = droppedObject.playerHeldBy.isInHangarShipRoom,
                        isInsideFactory = droppedObject.playerHeldBy.isInsideFactory
                    };
                }
                else if (dropableObjectDroppedData.ContainsKey(droppedObject.OwnerClientId))
                {
                    locationData = dropableObjectDroppedData[droppedObject.OwnerClientId];
                }
                else
                {
                    Plugin.Instance.Log.LogWarning($"Not handled non-player target {targetName} for inverse teleporter target");
                    return null;
                }
            }
            else
            {
                PlayerControllerB teleportedToPlayer = StartOfRound.Instance.mapScreen.targetedPlayer;

                if (teleportedToPlayer.isActiveAndEnabled && StartOfRound.Instance.allPlayerScripts.Contains(teleportedToPlayer))
                {
                    locationData = new LocationData
                    {
                        isInElevator = teleportedToPlayer.isInElevator,
                        isInHangarShipRoom = teleportedToPlayer.isInHangarShipRoom,
                        isInsideFactory = teleportedToPlayer.isInsideFactory
                    };

                    if (teleportedToPlayer.isPlayerDead)
                        targetPosition = teleportedToPlayer.deadBody.transform.position;
                }
                else
                {
                    Plugin.Instance.Log.LogWarning($"Invalid player target {targetName} for inverse teleporter target");
                    return null;
                }
            }

            return locationData;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GrabbableObject), "DiscardItem")]
        public static bool StoreDroppedItemLocation(GrabbableObject __instance)
        {
            PlayerControllerB player = __instance.playerHeldBy;
            if (player)
                dropableObjectDroppedData[__instance.OwnerClientId] = new LocationData {
                    isInElevator = player.isInElevator,
                    isInHangarShipRoom = player.isInHangarShipRoom,
                    isInsideFactory = player.isInsideFactory };

            return true;
        }

        [HarmonyPatch]
        public class BrazilNetworkManager
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(GameNetworkManager), "Start")]
            public static void Init()
            {
                if (brazilNetworkPrefab != null)
                    return;

                brazilNetworkPrefab = (GameObject)Plugin.Instance.MainAssetBundle.LoadAsset("BrazilTransmiterNetworkHandler");
                brazilNetworkPrefab.AddComponent<BrazilTransmiterNetworkHandler>();
                NetworkManager.Singleton.AddNetworkPrefab(brazilNetworkPrefab);
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(StartOfRound), "Awake")]
            static void SpawnNetworkHandler()
            {
                if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
                {
                    var networkHandlerHost = Object.Instantiate(brazilNetworkPrefab, Vector3.zero, Quaternion.identity);
                    networkHandlerHost.GetComponent<NetworkObject>().Spawn();
                }
            }

            public static GameObject brazilNetworkPrefab;
        }

        class BrazilTransmiterNetworkHandler : NetworkBehaviour
        {
            [ServerRpc(RequireOwnership = false)]
            public void BrazilTransmissionServerRPC(int clientId)
            {
                ClientRpcParams clientRpcParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new ulong[] { (ulong)clientId }
                    }
                };

                BrazilTransmissionClientRpc(clientRpcParams);
            }

            [ClientRpc]
            public void BrazilTransmissionClientRpc(ClientRpcParams clientRpcParams = default)
            {
                HUDManager.Instance.StartCoroutine(BrazilTransmissionClient());
            }

            private IEnumerator BrazilTransmissionClient()
            {
                HUDManager.Instance.signalTranslatorAnimator.SetBool("transmitting", true);
                HUDManager.Instance.signalTranslatorText.text = "Welcome to Brazil";
                yield return new WaitForSeconds(3.0f);
                HUDManager.Instance.signalTranslatorAnimator.SetBool("transmitting", false);
                yield break;
            }
        }
    }
}