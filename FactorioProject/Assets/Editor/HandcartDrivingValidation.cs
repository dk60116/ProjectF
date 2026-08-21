#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class HandcartDrivingValidation
{
    private const string HandcartPrefabPath = "Assets/MapObject/Cart/Handcart.prefab";
    private static readonly Vector3 ValidationOrigin = new Vector3(10000f, 0f, 10000f);

    [MenuItem("Tools/ProjectF/Validation/Handcart Driving")]
    public static void Validate()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandcartPrefabPath);
        Require(prefab != null, $"Handcart 프리팹을 찾을 수 없습니다: {HandcartPrefabPath}");

        GameObject instance = null;
        GameObject connectedInstance = null;
        GameObject obstacle = null;
        GameObject connectionRegistrationObstacle = null;
        GameObject pickupPlayerObject = null;
        GameObject thirdConnectionInstance = null;
        GameObject fourthConnectionInstance = null;
        try
        {
            instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "Handcart Driving Validation";
            Handcart handcart = instance.GetComponent<Handcart>();
            Require(handcart != null, "Handcart 컴포넌트가 없습니다.");
            BoxCollider handcartCollider = instance.GetComponent<BoxCollider>();
            Require(handcartCollider != null, "주행 충돌용 BoxCollider가 없습니다.");
            SerializedObject serializedHandcart = new SerializedObject(handcart);
            SerializedProperty itemPointsProperty = serializedHandcart.FindProperty("itemPoints");
            GameObject firstHandleObject = serializedHandcart.FindProperty("handleObject")?.objectReferenceValue as GameObject;
            SerializedProperty collisionSkinWidthProperty = serializedHandcart.FindProperty("collisionSkinWidth");
            Require(handcart is IPlayerItemStorage, "Handcart가 플레이어 아이템 저장소를 구현하지 않았습니다.");
            Require(
                handcart is IPersistentInstallationItemCollectionStorage,
                "Handcart가 다중 아이템 저장·복원을 구현하지 않았습니다.");
            Require(handcart.BoundItemDefinition != null, "Handcart ItemDefinition이 프리팹에 연결되지 않았습니다.");
            Require(
                handcart.Capacity == Mathf.Max(1, handcart.BoundItemDefinition.capacity),
                "Handcart가 ItemDefinition capacity를 스택 용량으로 사용하지 않습니다.");
            Require(itemPointsProperty != null && itemPointsProperty.arraySize > 0, "Handcart 적재 지점이 없습니다.");
            Require(firstHandleObject != null, "Handcart HandleObject가 연결되지 않았습니다.");
            serializedHandcart.FindProperty("blockWater").boolValue = false;
            serializedHandcart.ApplyModifiedPropertiesWithoutUndo();
            handcart.SetExcludeFromTerrainPersistence(true);
            instance.transform.SetPositionAndRotation(ValidationOrigin, Quaternion.identity);

            handcart.ConfigurePlacementRuntime(
                new Vector2Int(
                    Mathf.RoundToInt(ValidationOrigin.x),
                    Mathf.RoundToInt(ValidationOrigin.z)),
                0,
                new[]
                {
                    new Vector2Int(
                        Mathf.RoundToInt(ValidationOrigin.x),
                        Mathf.RoundToInt(ValidationOrigin.z))
                },
                1);

            int cargoItemId = ResolvePortableCargoItemId();
            Require(cargoItemId >= 0, "Handcart 검증에 사용할 휴대 가능 아이템을 찾지 못했습니다.");
            int expectedTotalCapacity = handcart.StackCount * handcart.Capacity;
            Require(expectedTotalCapacity > handcart.Capacity, "Handcart 적재 지점 수가 capacity 계산에 반영되지 않았습니다.");
            Require(
                handcart.TryAddItemStack(
                    cargoItemId,
                    expectedTotalCapacity + 1,
                    handcart.transform.position,
                    null,
                    0f,
                    out int addedCargoCount),
                "Handcart에 아이템을 적재하지 못했습니다.");
            Require(
                addedCargoCount == expectedTotalCapacity,
                "Handcart가 Item Point 수 x 스택 capacity를 초과하거나 덜 적재했습니다.");
            Require(
                handcart.StoredItemCount == expectedTotalCapacity,
                "Handcart 총 적재 수량이 Item Point 수 x 스택 capacity와 다릅니다.");

            var infoItemIds = new System.Collections.Generic.List<int>();
            var infoItemCounts = new System.Collections.Generic.List<int>();
            int objectInfoStackCount = handcart.CopyObjectInfoStacks(
                infoItemIds,
                infoItemCounts,
                handcart.StackCount);
            Require(objectInfoStackCount == handcart.StackCount, "InfoPanel용 Handcart 스택 수가 Item Point 수와 다릅니다.");
            for (int i = 0; i < objectInfoStackCount; i++)
            {
                Require(infoItemIds[i] == cargoItemId, "InfoPanel용 Handcart 아이템 ID가 적재 아이템과 다릅니다.");
                Require(infoItemCounts[i] == handcart.Capacity, "InfoPanel용 Handcart 스택 수량이 capacity와 다릅니다.");
            }

            var capturedCargoItemIds = new System.Collections.Generic.List<int>();
            handcart.CapturePersistentStoredItemIds(capturedCargoItemIds);
            Require(capturedCargoItemIds.Count == expectedTotalCapacity, "Handcart 적재 상태 캡처 수량이 다릅니다.");
            handcart.ApplyPersistentStoredItemIds(Array.Empty<int>());
            Require(handcart.StoredItemCount == 0, "Handcart 적재 상태 초기화에 실패했습니다.");
            handcart.ApplyPersistentStoredItemIds(capturedCargoItemIds);
            Require(handcart.StoredItemCount == expectedTotalCapacity, "Handcart 적재 상태 복원에 실패했습니다.");

            handcart.ApplyPersistentStoredItemIds(Array.Empty<int>());
            for (int stackIndex = 0; stackIndex < handcart.StackCount; stackIndex++)
            {
                Require(
                    handcart.TryAddItemStack(
                        10000 + stackIndex,
                        1,
                        handcart.transform.position,
                        null,
                        0f,
                        out int addedDistinctItemCount)
                    && addedDistinctItemCount == 1,
                    $"Handcart의 {stackIndex}번 Item Point에 독립 스택을 만들지 못했습니다.");
            }

            Require(
                !handcart.TryAddItemStack(
                    20000,
                    1,
                    handcart.transform.position,
                    null,
                    0f,
                    out int addedOverflowStackCount)
                && addedOverflowStackCount == 0,
                "Handcart가 Item Point 수보다 많은 종류의 스택을 만들었습니다.");

            if (handcart.Capacity > 1)
            {
                Require(
                    handcart.TryAddItemStack(
                        10000,
                        handcart.Capacity,
                        handcart.transform.position,
                        null,
                        0f,
                        out int addedExistingStackCount)
                    && addedExistingStackCount == handcart.Capacity - 1,
                    "Handcart가 같은 아이템을 기존 스택의 capacity까지 채우지 못했습니다.");
            }

            handcart.ApplyPersistentStoredItemIds(Array.Empty<int>());
            const int nearbyPickupItemId = 30000;
            const int distantPickupItemId = 30001;
            Require(
                handcart.TryAddItemStack(
                    nearbyPickupItemId,
                    1,
                    handcart.transform.position,
                    null,
                    0f,
                    out int addedNearbyPickupItemCount)
                && addedNearbyPickupItemCount == 1,
                "Handcart 근거리 회수 검증 아이템을 적재하지 못했습니다.");
            Require(
                handcart.TryAddItemStack(
                    distantPickupItemId,
                    1,
                    handcart.transform.position,
                    null,
                    0f,
                    out int addedDistantPickupItemCount)
                && addedDistantPickupItemCount == 1,
                "Handcart 원거리 회수 검증 아이템을 적재하지 못했습니다.");

            Transform nearbyPickupPoint = itemPointsProperty.GetArrayElementAtIndex(0).objectReferenceValue as Transform;
            Require(nearbyPickupPoint != null, "Handcart 근거리 회수 검증 Item Point가 없습니다.");
            pickupPlayerObject = new GameObject("Handcart Pickup Validation Player");
            Player pickupPlayer = pickupPlayerObject.AddComponent<Player>();
            GameObject pickupHandObject = new GameObject("Handcart Pickup Validation Hand Item");
            pickupHandObject.transform.SetParent(pickupPlayerObject.transform, false);
            pickupHandObject.AddComponent<MeshFilter>();
            pickupHandObject.AddComponent<MeshRenderer>();
            PortableObject pickupHandPortableObject = pickupHandObject.AddComponent<PortableObject>();
            pickupHandObject.SetActive(false);
            SerializedObject serializedPickupPlayer = new SerializedObject(pickupPlayer);
            SerializedProperty handStackProperty = serializedPickupPlayer.FindProperty("handStack");
            Require(handStackProperty != null, "Player handStack 필드를 찾을 수 없습니다.");
            handStackProperty.arraySize = 1;
            handStackProperty.GetArrayElementAtIndex(0).objectReferenceValue = pickupHandPortableObject;
            serializedPickupPlayer.ApplyModifiedPropertiesWithoutUndo();
            Require(
                handcart.TryPreviewPickupItems(
                    pickupPlayer,
                    nearbyPickupPoint.position,
                    0.05f,
                    -1,
                    out int nearbyPreviewItemId,
                    out _)
                && nearbyPreviewItemId == nearbyPickupItemId,
                "Handcart가 플레이어와 가까운 아이템 대신 마지막 적재 아이템을 회수 대상으로 선택했습니다.");

            handcart.ApplyPersistentStoredItemIds(Array.Empty<int>());
            Require(
                handcart.TryAddItemStack(
                    cargoItemId,
                    1,
                    nearbyPickupPoint.position,
                    null,
                    0f,
                    out int addedHandPickupItemCount)
                && addedHandPickupItemCount == 1,
                "Hand 회수 검증용 아이템을 Handcart에 적재하지 못했습니다.");
            Require(pickupPlayer.CanAcceptHandObject(cargoItemId), "빈 Hand가 검증 아이템을 받을 수 없습니다.");
            Require(
                handcart.TryPickupOneItemToHand(
                    pickupPlayer,
                    nearbyPickupPoint.position,
                    0.05f,
                    cargoItemId),
                "Hand에서 Handcart 아이템을 회수하지 못했습니다.");
            Require(pickupPlayer.GetHandItemCount() == 1, "Handcart 회수 아이템이 Hand에 들어가지 않았습니다.");
            Require(handcart.StoredItemCount == 0, "Hand로 회수한 아이템이 Handcart에 남아 있습니다.");

            FindWheels(instance.transform, out Transform firstWheel, out Transform secondWheel);
            Require(firstWheel != null && secondWheel != null, "좌우 바퀴 Transform을 찾을 수 없습니다.");
            Require(
                handcart.TryGetPlayerPoint(0, out Transform playerAnimationFacing),
                "Handcart 플레이어 탑승 지점을 찾을 수 없습니다.");
            pickupPlayer.transform.position = handcart.transform.position - handcart.transform.forward;
            Require(
                !handcart.CanPlayerDock(pickupPlayer),
                "Handcart 운전대 반대쪽에서도 운전을 시작할 수 있습니다.");
            pickupPlayer.transform.position = playerAnimationFacing.position + handcart.transform.forward * 0.1f;
            Require(
                handcart.CanPlayerDock(pickupPlayer),
                "단독 Handcart의 운전대 끝에서 운전을 시작할 수 없습니다.");
            Quaternion wheelRotationBeforeDrive = firstWheel.localRotation;
            Vector3 firstWheelUpBeforeDrive = firstWheel.TransformDirection(Vector3.up);
            Vector3 secondWheelUpBeforeDrive = secondWheel.TransformDirection(Vector3.up);

            handcart.HandleMountedInput(Vector3.forward, 3f, 0.05f, null);
            Require(handcart.CurrentVehicleSignedSpeed > 0f, "전진 입력의 차체 속도 부호가 양수가 아닙니다.");
            Require(
                handcart.ResolveSignedSpeedRelativeToFacing(playerAnimationFacing) < 0f,
                "차체와 반대로 선 플레이어에게 전진 입력이 뒷걸음 애니메이션으로 전달되지 않습니다.");
            Vector3 firstWheelUpAfterDrive = firstWheel.TransformDirection(Vector3.up);
            Vector3 secondWheelUpAfterDrive = secondWheel.TransformDirection(Vector3.up);
            Require(
                Vector3.Dot(firstWheelUpAfterDrive - firstWheelUpBeforeDrive, handcart.transform.forward) > 0f,
                "전진할 때 바퀴가 실제 구름 방향과 반대로 회전합니다.");
            Require(
                Vector3.Dot(secondWheelUpAfterDrive - secondWheelUpBeforeDrive, handcart.transform.forward) > 0f,
                "미러링된 바퀴가 실제 구름 방향과 반대로 회전합니다.");
            Require(
                Vector3.Dot(firstWheelUpAfterDrive.normalized, secondWheelUpAfterDrive.normalized) > 0.999f,
                "좌우 바퀴가 서로 반대 방향으로 회전합니다.");

            Drive(handcart, Vector3.forward, 10);
            Require(
                handcart.transform.position.z > ValidationOrigin.z + 0.65f,
                "전진 입력으로 Handcart가 충분히 이동하지 않았습니다.");
            Require(
                Quaternion.Angle(wheelRotationBeforeDrive, firstWheel.localRotation) > 1f,
                "주행 거리만큼 바퀴가 회전하지 않았습니다.");

            ResetPose(handcart);
            Drive(handcart, Vector3.right, 5);
            Require(
                Vector3.Angle(handcart.transform.forward, Vector3.forward) > 35f,
                "좌우 입력 방향으로 조향되지 않았습니다.");
            Require(
                handcart.transform.position.x > ValidationOrigin.x + 0.05f,
                "조향한 방향으로 이동하지 않았습니다.");

            ResetPose(handcart);
            Drive(handcart, Vector3.back, 10);
            Require(handcart.CurrentVehicleSignedSpeed < 0f, "후진 입력의 차체 속도 부호가 음수가 아닙니다.");
            Require(
                handcart.ResolveSignedSpeedRelativeToFacing(playerAnimationFacing) > 0f,
                "차체와 반대로 선 플레이어에게 후진 입력이 전진 애니메이션으로 전달되지 않습니다.");
            Require(
                handcart.transform.position.z < ValidationOrigin.z - 0.65f,
                "후진 입력으로 이동하지 않았습니다.");
            Require(
                Vector3.Angle(handcart.transform.forward, Vector3.forward) < 1f,
                "정반대 입력에서 차체를 돌리지 않고 후진해야 합니다.");

            ResetPose(handcart);
            obstacle = new GameObject("Handcart Validation Obstacle");
            obstacle.layer = LayerMask.NameToLayer("Object");
            obstacle.transform.position = ValidationOrigin + new Vector3(0f, 0.5f, 0.55f);
            BoxCollider obstacleCollider = obstacle.AddComponent<BoxCollider>();
            obstacleCollider.size = new Vector3(2f, 1f, 0.25f);
            Physics.SyncTransforms();

            Drive(handcart, Vector3.forward, 12);
            float allowedCollisionSkin = collisionSkinWidthProperty != null
                ? Mathf.Max(0f, collisionSkinWidthProperty.floatValue) + 0.005f
                : 0.035f;
            Require(
                handcartCollider.bounds.max.z
                <= obstacleCollider.bounds.min.z + allowedCollisionSkin,
                $"장애물 앞에서 Handcart가 멈추지 않았습니다. "
                + $"cartMaxZ={handcartCollider.bounds.max.z}, obstacleMinZ={obstacleCollider.bounds.min.z}, "
                + $"speed={handcart.CurrentVehicleSpeed}");
            Require(handcart.CurrentVehicleSpeed <= 0.0001f, "충돌 후 주행 속도가 초기화되지 않았습니다.");

            UnityEngine.Object.DestroyImmediate(obstacle);
            obstacle = null;
            ResetPose(handcart);
            Vector3 movedSourcePosition = ValidationOrigin + new Vector3(0.35f, 0f, 0.3f);
            Quaternion movedSourceRotation = Quaternion.Euler(0f, 35f, 0f);
            handcart.transform.SetPositionAndRotation(movedSourcePosition, movedSourceRotation);
            Vector2Int movedSourceCoordinate = new Vector2Int(
                Mathf.RoundToInt(movedSourcePosition.x),
                Mathf.RoundToInt(movedSourcePosition.z));
            handcart.ConfigurePlacementRuntime(
                movedSourceCoordinate,
                0,
                new[] { movedSourceCoordinate },
                handcart.RuntimePlacementSequence);

            connectedInstance = UnityEngine.Object.Instantiate(prefab);
            connectedInstance.name = "Connected Handcart Driving Validation";
            Handcart connectedHandcart = connectedInstance.GetComponent<Handcart>();
            BoxCollider connectedCollider = connectedInstance.GetComponent<BoxCollider>();
            Require(connectedHandcart != null && connectedCollider != null, "연결 검증용 Handcart 구성이 올바르지 않습니다.");
            SerializedObject serializedConnectedHandcart = new SerializedObject(connectedHandcart);
            GameObject connectedHandleObject = serializedConnectedHandcart.FindProperty("handleObject")?.objectReferenceValue as GameObject;
            SerializedProperty connectedMassProperty = serializedConnectedHandcart.FindProperty("vehicleMass");
            Require(connectedMassProperty != null, "Vehicle 공통 mass 필드를 찾을 수 없습니다.");
            Require(connectedHandleObject != null, "연결 검증용 Handcart HandleObject가 연결되지 않았습니다.");
            connectedMassProperty.floatValue = 20f;
            serializedConnectedHandcart.FindProperty("blockWater").boolValue = false;
            serializedConnectedHandcart.ApplyModifiedPropertiesWithoutUndo();
            connectedHandcart.SetExcludeFromTerrainPersistence(true);
            Vector3 connectedStartPosition = movedSourcePosition + movedSourceRotation * Vector3.forward;
            Vector3 connectedPlacementPosition = new Vector3(
                Mathf.Round(connectedStartPosition.x),
                connectedStartPosition.y - 0.2f,
                Mathf.Round(connectedStartPosition.z));
            connectedInstance.transform.SetPositionAndRotation(
                connectedPlacementPosition,
                Quaternion.Euler(0f, 110f, 0f));
            Vector2Int connectedCoordinate = new Vector2Int(
                Mathf.RoundToInt(connectedPlacementPosition.x),
                Mathf.RoundToInt(connectedPlacementPosition.z));
            connectedHandcart.ConfigurePlacementRuntime(
                connectedCoordinate,
                0,
                new[] { connectedCoordinate },
                2);
            Physics.SyncTransforms();

            Require(
                Handcart.TryResolveConnectionPreviewPose(
                    connectedHandcart,
                    handcart,
                    connectedPlacementPosition,
                    out int previewConnectionSourceSide,
                    out Vector3 previewSnappedPosition,
                    out Quaternion previewSnappedRotation),
                "Handcart 블루프린트의 연결 미리보기 위치를 계산하지 못했습니다.");
            Require(previewConnectionSourceSide == 1, "Handcart 블루프린트 연결면을 잘못 판정했습니다.");
            Require(
                Vector3.Distance(previewSnappedPosition, connectedStartPosition) < 0.01f,
                "Handcart 블루프린트가 기존 Handcart의 연결축 위치로 스냅되지 않았습니다.");
            Require(
                Quaternion.Angle(previewSnappedRotation, handcart.transform.rotation) < 0.01f,
                "Handcart 블루프린트가 기존 Handcart의 각도로 스냅되지 않았습니다.");
            Require(
                Handcart.CanConnectByPose(handcart, connectedHandcart),
                "주행 후 그리드에서 벗어난 Handcart 주변의 새 Handcart를 연결 가능하게 판정하지 못했습니다.");
            Require(handcart.ConnectTo(connectedHandcart), "Handcart끼리 연결하지 못했습니다.");
            Require(
                Vector3.Distance(connectedHandcart.transform.position, connectedStartPosition) < 0.01f,
                "연결 시 새 Handcart가 기존 Handcart의 3차원 연결축 위치에 맞춰지지 않았습니다.");
            Require(
                Quaternion.Angle(handcart.transform.rotation, connectedHandcart.transform.rotation) < 0.01f,
                "연결 시 새 Handcart의 각도가 기존 Handcart에 맞춰지지 않았습니다.");
            Require(
                handcart.ConnectedHandcarts.Count == 1
                && connectedHandcart.ConnectedHandcarts.Count == 1,
                "Handcart 연결이 양방향으로 등록되지 않았습니다.");
            Require(!firstHandleObject.activeSelf, "연결부에 위치한 첫 번째 Handcart의 HandleObject가 비활성화되지 않았습니다.");
            Require(connectedHandleObject.activeSelf, "연결부 반대쪽인 두 번째 Handcart의 HandleObject가 비활성화되었습니다.");
            Require(
                !handcart.CanPlayerDock(pickupPlayer),
                "운전대가 제거된 Handcart에서 운전을 시작할 수 있습니다.");
            Require(
                connectedHandcart.TryGetPlayerPoint(0, out Transform connectedPlayerPoint),
                "연결된 Handcart의 운전 지점을 찾을 수 없습니다.");
            pickupPlayer.transform.position = connectedPlayerPoint.position + connectedHandcart.transform.forward * 0.1f;
            Require(
                connectedHandcart.CanPlayerDock(pickupPlayer),
                "연결된 수레 묶음의 유일한 운전대 끝에서 운전을 시작할 수 없습니다.");
            handcart.DisconnectFrom(connectedHandcart);
            Require(
                firstHandleObject.activeSelf && connectedHandleObject.activeSelf,
                "연결 해제 후 Handcart HandleObject가 복원되지 않았습니다.");

            connectionRegistrationObstacle = new GameObject("Handcart Connection Registration Obstacle");
            connectionRegistrationObstacle.layer = LayerMask.NameToLayer("Object");
            connectionRegistrationObstacle.transform.position = connectedHandcart.transform.position + Vector3.up * 0.5f;
            BoxCollider connectionRegistrationCollider = connectionRegistrationObstacle.AddComponent<BoxCollider>();
            connectionRegistrationCollider.size = Vector3.one;
            Physics.SyncTransforms();
            Require(
                handcart.ConnectTo(connectedHandcart),
                "이미 스냅된 Handcart의 최종 연결 등록이 주변 충돌 판정에 막혔습니다.");
            handcart.ClearHandcartConnections();
            Require(
                handcart.ConnectedHandcarts.Count == 0
                && connectedHandcart.ConnectedHandcarts.Count == 0
                && firstHandleObject.activeSelf
                && connectedHandleObject.activeSelf,
                "Handcart 연결 전체 해제 후 연결부 HandleObject가 복원되지 않았습니다.");
            UnityEngine.Object.DestroyImmediate(connectionRegistrationObstacle);
            connectionRegistrationObstacle = null;
            handcart.ConnectToNearbyActiveHandcarts();
            Require(
                handcart.ConnectedHandcarts.Count == 1
                && connectedHandcart.ConnectedHandcarts.Count == 1,
                "복원된 인접 Handcart의 자동 재연결에 실패했습니다.");
            handcart.DisconnectFrom(connectedHandcart);

            Vector3 reverseConnectedStartPosition = movedSourcePosition + movedSourceRotation * Vector3.back;
            Vector3 reverseConnectedPlacementPosition = new Vector3(
                Mathf.Round(reverseConnectedStartPosition.x),
                reverseConnectedStartPosition.y,
                Mathf.Round(reverseConnectedStartPosition.z));
            Vector2Int reverseConnectedCoordinate = new Vector2Int(
                Mathf.RoundToInt(reverseConnectedPlacementPosition.x),
                Mathf.RoundToInt(reverseConnectedPlacementPosition.z));
            connectedInstance.transform.SetPositionAndRotation(
                reverseConnectedPlacementPosition,
                Quaternion.Euler(0f, 110f, 0f));
            connectedHandcart.ConfigurePlacementRuntime(
                reverseConnectedCoordinate,
                0,
                new[] { reverseConnectedCoordinate },
                2);
            Physics.SyncTransforms();
            Require(
                Handcart.TryResolveConnectionPreviewPose(
                    connectedHandcart,
                    handcart,
                    reverseConnectedPlacementPosition,
                    out int reversePreviewConnectionSourceSide,
                    out _,
                    out _)
                && reversePreviewConnectionSourceSide == -1,
                "반대쪽 Handcart 블루프린트 연결면을 잘못 판정했습니다.");
            Require(handcart.ConnectTo(connectedHandcart), "반대쪽 Handcart 연결에 실패했습니다.");
            Require(
                Vector3.Distance(
                    connectedHandcart.transform.position,
                    reverseConnectedStartPosition) < 0.01f,
                "반대쪽 Handcart 연결 위치가 연결점에서 어긋났습니다.");
            Require(
                Quaternion.Angle(
                    handcart.transform.rotation,
                    connectedHandcart.transform.rotation) < 0.01f,
                "반대쪽 Handcart 연결 각도가 기존 수레와 맞지 않습니다.");
            Require(
                firstHandleObject.activeSelf && !connectedHandleObject.activeSelf,
                "반대쪽 연결에서 설치 순서가 아니라 연결면 기준으로 HandleObject를 비활성화하지 않았습니다.");
            handcart.DisconnectFrom(connectedHandcart);

            connectedInstance.transform.SetPositionAndRotation(
                connectedPlacementPosition,
                Quaternion.Euler(0f, 110f, 0f));
            connectedHandcart.ConfigurePlacementRuntime(
                connectedCoordinate,
                0,
                new[] { connectedCoordinate },
                2);
            Physics.SyncTransforms();
            Require(handcart.ConnectTo(connectedHandcart), "HandleObject 방향 검증 후 Handcart 재연결에 실패했습니다.");
            Require(
                !firstHandleObject.activeSelf && connectedHandleObject.activeSelf,
                "Handcart 재연결 후 연결면 기준 HandleObject 상태가 올바르지 않습니다.");
            connectedInstance.SetActive(false);
            Require(
                handcart.ConnectedHandcarts.Count == 1
                && connectedHandcart.ConnectedHandcarts.Count == 1,
                "청크 휴면을 모사한 임시 비활성화 중 Handcart 연결이 해제되었습니다.");
            connectedInstance.SetActive(true);
            Physics.SyncTransforms();
            Require(
                handcart.ConnectedHandcarts.Count == 1
                && connectedHandcart.ConnectedHandcarts.Count == 1,
                "청크 휴면 복귀 후 Handcart 연결이 복원되지 않았습니다.");
            Require(
                !firstHandleObject.activeSelf && connectedHandleObject.activeSelf,
                "청크 휴면 복귀 후 연결면 HandleObject 상태가 바뀌었습니다.");
            Require(
                Mathf.Approximately(
                    handcart.EffectiveVehicleMaxSpeed,
                    handcart.VehicleMaxSpeed * connectedHandcart.VehicleLoadSpeedMultiplier),
                "연결된 Handcart의 Vehicle mass가 최고 속도에 반영되지 않았습니다.");

            Vector3 initialConnectedOffset = connectedHandcart.transform.position - handcart.transform.position;
            Vector3 straightDriveDirection = connectedHandcart.transform.forward;
            Drive(connectedHandcart, straightDriveDirection, 6);
            Require(
                handcart.ConnectedHandcarts.Count == 1
                && connectedHandcart.ConnectedHandcarts.Count == 1,
                "직선 운전 중 Handcart 연결이 해제되었습니다.");
            Require(
                Vector3.Distance(connectedHandcart.transform.position, connectedStartPosition) > 0.2f,
                "연결된 Handcart가 운전 중 함께 이동하지 않았습니다.");
            Require(
                Vector3.Distance(
                    connectedHandcart.transform.position - handcart.transform.position,
                    initialConnectedOffset) < 0.01f,
                "직선 주행 중 연결된 Handcart 간격이 변했습니다.");

            Drive(connectedHandcart, Vector3.right, 5);
            Require(
                handcart.ConnectedHandcarts.Count == 1
                && connectedHandcart.ConnectedHandcarts.Count == 1,
                "조향 운전 중 Handcart 연결이 해제되었습니다.");
            Require(
                Mathf.Abs(
                    Vector3.Distance(handcart.transform.position, connectedHandcart.transform.position)
                    - initialConnectedOffset.magnitude) < 0.01f,
                "조향 중 연결된 Handcart 간격이 변했습니다.");
            Require(
                Quaternion.Angle(handcart.transform.rotation, connectedHandcart.transform.rotation) < 0.1f,
                "조향 중 연결된 Handcart의 방향이 달라졌습니다.");

            ResetCoupledPose(handcart, connectedHandcart);
            thirdConnectionInstance = UnityEngine.Object.Instantiate(prefab);
            fourthConnectionInstance = UnityEngine.Object.Instantiate(prefab);
            Handcart thirdHandcart = thirdConnectionInstance.GetComponent<Handcart>();
            Handcart fourthHandcart = fourthConnectionInstance.GetComponent<Handcart>();
            Require(thirdHandcart != null && fourthHandcart != null, "수레 묶음 연결 검증용 Handcart를 만들지 못했습니다.");
            SerializedObject serializedThirdHandcart = new SerializedObject(thirdHandcart);
            SerializedObject serializedFourthHandcart = new SerializedObject(fourthHandcart);
            serializedThirdHandcart.FindProperty("blockWater").boolValue = false;
            serializedFourthHandcart.FindProperty("blockWater").boolValue = false;
            GameObject thirdHandleObject = serializedThirdHandcart.FindProperty("handleObject")?.objectReferenceValue as GameObject;
            GameObject fourthHandleObject = serializedFourthHandcart.FindProperty("handleObject")?.objectReferenceValue as GameObject;
            serializedThirdHandcart.ApplyModifiedPropertiesWithoutUndo();
            serializedFourthHandcart.ApplyModifiedPropertiesWithoutUndo();
            Require(thirdHandleObject != null && fourthHandleObject != null, "수레 묶음 연결 검증용 HandleObject가 없습니다.");
            thirdHandcart.SetExcludeFromTerrainPersistence(true);
            fourthHandcart.SetExcludeFromTerrainPersistence(true);

            Vector3 thirdStartPosition = ValidationOrigin + Vector3.forward * 2.08f;
            Vector3 fourthStartPosition = ValidationOrigin + Vector3.forward * 3.08f;
            Quaternion oppositeRotation = Quaternion.Euler(0f, 180f, 0f);
            thirdConnectionInstance.transform.SetPositionAndRotation(thirdStartPosition, oppositeRotation);
            fourthConnectionInstance.transform.SetPositionAndRotation(fourthStartPosition, oppositeRotation);
            Vector2Int thirdCoordinate = new Vector2Int(
                Mathf.RoundToInt(thirdStartPosition.x),
                Mathf.RoundToInt(thirdStartPosition.z));
            Vector2Int fourthCoordinate = new Vector2Int(
                Mathf.RoundToInt(fourthStartPosition.x),
                Mathf.RoundToInt(fourthStartPosition.z));
            thirdHandcart.ConfigurePlacementRuntime(thirdCoordinate, 2, new[] { thirdCoordinate }, 3);
            fourthHandcart.ConfigurePlacementRuntime(fourthCoordinate, 2, new[] { fourthCoordinate }, 4);
            Physics.SyncTransforms();
            Require(thirdHandcart.ConnectTo(fourthHandcart), "반대 방향의 두 번째 Handcart 묶음을 만들지 못했습니다.");
            Require(
                connectedHandcart.ConnectTo(thirdHandcart),
                "방향과 위치가 조금 어긋난 Handcart 묶음끼리 연결하지 못했습니다.");
            Require(
                handcart.ConnectedHandcarts.Count == 1
                && connectedHandcart.ConnectedHandcarts.Count == 2
                && thirdHandcart.ConnectedHandcarts.Count == 2
                && fourthHandcart.ConnectedHandcarts.Count == 1,
                "Handcart 묶음 연결 그래프가 올바르지 않습니다.");
            Require(
                Vector3.Distance(connectedHandcart.transform.position, thirdHandcart.transform.position) < 1.01f
                && Vector3.Distance(thirdHandcart.transform.position, fourthHandcart.transform.position) < 1.01f,
                "연결된 Handcart 묶음의 간격이 스냅되지 않았습니다.");
            Require(
                Quaternion.Angle(handcart.transform.rotation, thirdHandcart.transform.rotation) < 0.01f
                && Quaternion.Angle(handcart.transform.rotation, fourthHandcart.transform.rotation) < 0.01f,
                "연결된 Handcart 묶음의 방향이 기존 묶음에 맞춰지지 않았습니다.");
            Require(
                !firstHandleObject.activeSelf
                && !connectedHandleObject.activeSelf
                && !thirdHandleObject.activeSelf
                && fourthHandleObject.activeSelf,
                "여러 Handcart 묶음을 연결한 뒤 끝단 운전대 하나만 유지되지 않았습니다.");

            Vector3 alignedConnectedPosition = connectedHandcart.transform.position;
            connectedHandcart.transform.SetPositionAndRotation(
                handcart.transform.position
                + handcart.transform.right * handcart.ConnectionCenterDistance,
                handcart.transform.rotation);
            Physics.SyncTransforms();
            connectedInstance.SetActive(false);
            connectedInstance.SetActive(true);
            Physics.SyncTransforms();
            Require(
                !firstHandleObject.activeSelf
                && !connectedHandleObject.activeSelf
                && !thirdHandleObject.activeSelf
                && fourthHandleObject.activeSelf,
                "연결축이 일시적으로 어긋났을 때 운전대가 바깥 끝단이 아닌 연결 묶음 중간에 생겼습니다.");
            connectedHandcart.transform.SetPositionAndRotation(
                alignedConnectedPosition,
                handcart.transform.rotation);
            Physics.SyncTransforms();

            connectedHandcart.DisconnectFrom(thirdHandcart);
            thirdHandcart.DisconnectFrom(fourthHandcart);

            Vector3 reverseGroupEndpointStart = ValidationOrigin + Vector3.back * 1.08f;
            Vector3 reverseGroupNeighborStart = ValidationOrigin + Vector3.back * 0.08f;
            thirdConnectionInstance.transform.SetPositionAndRotation(
                reverseGroupEndpointStart,
                oppositeRotation);
            fourthConnectionInstance.transform.SetPositionAndRotation(
                reverseGroupNeighborStart,
                oppositeRotation);
            Vector2Int reverseGroupEndpointCoordinate = new Vector2Int(
                Mathf.RoundToInt(reverseGroupEndpointStart.x),
                Mathf.RoundToInt(reverseGroupEndpointStart.z));
            Vector2Int reverseGroupNeighborCoordinate = new Vector2Int(
                Mathf.RoundToInt(reverseGroupNeighborStart.x),
                Mathf.RoundToInt(reverseGroupNeighborStart.z));
            thirdHandcart.ConfigurePlacementRuntime(
                reverseGroupEndpointCoordinate,
                2,
                new[] { reverseGroupEndpointCoordinate },
                3);
            fourthHandcart.ConfigurePlacementRuntime(
                reverseGroupNeighborCoordinate,
                2,
                new[] { reverseGroupNeighborCoordinate },
                4);
            Physics.SyncTransforms();
            Require(
                thirdHandcart.ConnectTo(fourthHandcart),
                "반대쪽 연결 검증용 Handcart 묶음을 만들지 못했습니다.");

            fourthHandcart.transform.position += Vector3.up * 0.2f;
            Physics.SyncTransforms();
            Require(
                handcart.ConnectTo(thirdHandcart),
                "반대쪽에서 방향과 위치가 어긋난 Handcart 묶음을 연결하지 못했습니다.");

            Vector3 expectedReverseEndpoint = handcart.transform.position
                                              - handcart.transform.forward
                                              * handcart.ConnectionCenterDistance;
            Vector3 expectedReverseNeighbor = expectedReverseEndpoint
                                              - handcart.transform.forward
                                              * handcart.ConnectionCenterDistance
                                              + Vector3.up * 0.2f;
            Require(
                Vector3.Distance(
                    thirdHandcart.transform.position,
                    expectedReverseEndpoint) < 0.01f
                && Vector3.Distance(
                    fourthHandcart.transform.position,
                    expectedReverseNeighbor) < 0.01f,
                "반대쪽 Handcart 묶음의 연결 위치가 수평 회전 기준에서 어긋났습니다.");
            Require(
                Quaternion.Angle(
                    handcart.transform.rotation,
                    thirdHandcart.transform.rotation) < 0.01f
                && Quaternion.Angle(
                    handcart.transform.rotation,
                    fourthHandcart.transform.rotation) < 0.01f,
                "반대쪽 Handcart 묶음의 연결 각도가 기존 묶음에 맞지 않습니다.");

            handcart.DisconnectFrom(thirdHandcart);
            UnityEngine.Object.DestroyImmediate(thirdConnectionInstance);
            thirdConnectionInstance = null;
            UnityEngine.Object.DestroyImmediate(fourthConnectionInstance);
            fourthConnectionInstance = null;
            ResetCoupledPose(handcart, connectedHandcart);
            obstacle = new GameObject("Connected Handcart Validation Obstacle");
            obstacle.layer = LayerMask.NameToLayer("Object");
            obstacle.transform.position = ValidationOrigin + new Vector3(0f, 0.5f, 1.55f);
            obstacleCollider = obstacle.AddComponent<BoxCollider>();
            obstacleCollider.size = new Vector3(2f, 1f, 0.25f);
            Physics.SyncTransforms();
            Drive(connectedHandcart, Vector3.forward, 12);
            Require(
                connectedCollider.bounds.max.z
                <= obstacleCollider.bounds.min.z + allowedCollisionSkin,
                "앞쪽에 연결된 Handcart가 장애물을 통과했습니다.");
            Require(connectedHandcart.CurrentVehicleSpeed <= 0.0001f, "연결 차량 충돌 후 운전 Handcart 속도가 초기화되지 않았습니다.");

            Debug.Log("Handcart validation passed: ItemData stack capacity/InfoPanel 스택 표기/명시적 Item Point/근거리 회수 선택/Hand 회수/적재 상태 복원/플레이어 기준 애니메이션 방향/전진/조향/후진/좌우 바퀴 방향/단독 및 연결 차량 충돌/Handcart 최종 연결 등록 및 복원 자동 재연결/연결 묶음당 단일 HandleObject 및 운전대 끝 운전 제한/연결 해제 복원/블루프린트 연결 위치 및 각도 스냅/청크 휴면 및 운전 중 연결 유지/주행 후 연결 위치 및 각도 스냅/Vehicle mass 감속");
        }
        finally
        {
            CleanupValidationPersistence();

            if (obstacle != null)
            {
                UnityEngine.Object.DestroyImmediate(obstacle);
            }

            if (connectionRegistrationObstacle != null)
            {
                UnityEngine.Object.DestroyImmediate(connectionRegistrationObstacle);
            }

            if (pickupPlayerObject != null)
            {
                UnityEngine.Object.DestroyImmediate(pickupPlayerObject);
            }

            if (thirdConnectionInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(thirdConnectionInstance);
            }

            if (fourthConnectionInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(fourthConnectionInstance);
            }

            if (instance != null)
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            if (connectedInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(connectedInstance);
            }
        }
    }

    [MenuItem("Tools/ProjectF/Validation/Handcart Driving", true)]
    private static bool CanValidate()
    {
        return Application.isPlaying;
    }

    private static int ResolvePortableCargoItemId()
    {
        ItemManager itemManager = GameManager.Instance != null
            ? GameManager.Instance.ItemManger
            : UnityEngine.Object.FindAnyObjectByType<ItemManager>();
        if (itemManager == null || itemManager.ItemDefinitions == null)
        {
            return -1;
        }

        for (int i = 0; i < itemManager.ItemDefinitions.Count; i++)
        {
            ItemDefinition definition = itemManager.ItemDefinitions[i];
            if (definition != null
                && definition.id >= 0
                && definition.portableMesh != null
                && definition.portableMat != null
                && !InputOutputModule.IsFluidItemId(definition.id))
            {
                return definition.id;
            }
        }

        return -1;
    }

    private static void CleanupValidationPersistence()
    {
        TerrainGenerator terrain = TerrainGenerator.Active;
        if (terrain == null)
        {
            return;
        }

        Vector2Int center = new Vector2Int(
            Mathf.RoundToInt(ValidationOrigin.x),
            Mathf.RoundToInt(ValidationOrigin.z));
        for (int y = center.y - 2; y <= center.y + 2; y++)
        {
            for (int x = center.x - 2; x <= center.x + 2; x++)
            {
                terrain.RemoveInstallationPersistence(new Vector2Int(x, y));
            }
        }
    }

    private static void Drive(Handcart handcart, Vector3 direction, int stepCount)
    {
        for (int i = 0; i < stepCount; i++)
        {
            handcart.HandleMountedInput(direction, 3f, 0.1f, null);
        }
    }

    private static void ResetPose(Handcart handcart)
    {
        handcart.NotifyPlayerDismounted(null);
        handcart.transform.SetPositionAndRotation(ValidationOrigin, Quaternion.identity);
        Vector2Int coordinate = new Vector2Int(
            Mathf.RoundToInt(ValidationOrigin.x),
            Mathf.RoundToInt(ValidationOrigin.z));
        handcart.ConfigurePlacementRuntime(
            coordinate,
            0,
            new[] { coordinate },
            handcart.RuntimePlacementSequence);
        Physics.SyncTransforms();
    }

    private static void ResetCoupledPose(Handcart handcart, Handcart connectedHandcart)
    {
        ResetPose(handcart);
        connectedHandcart.NotifyPlayerDismounted(null);
        Vector3 connectedPosition = ValidationOrigin + Vector3.forward;
        connectedHandcart.transform.SetPositionAndRotation(connectedPosition, Quaternion.identity);
        Vector2Int connectedCoordinate = new Vector2Int(
            Mathf.RoundToInt(connectedPosition.x),
            Mathf.RoundToInt(connectedPosition.z));
        connectedHandcart.ConfigurePlacementRuntime(
            connectedCoordinate,
            0,
            new[] { connectedCoordinate },
            connectedHandcart.RuntimePlacementSequence);
        Physics.SyncTransforms();
    }

    private static void FindWheels(Transform root, out Transform firstWheel, out Transform secondWheel)
    {
        firstWheel = null;
        secondWheel = null;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null
                || !candidate.name.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (firstWheel == null)
            {
                firstWheel = candidate;
            }
            else
            {
                secondWheel = candidate;
                return;
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
#endif
