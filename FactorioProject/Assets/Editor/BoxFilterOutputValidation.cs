using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class BoxFilterOutputValidation
{
    [MenuItem("Tools/ProjectF/Validation/Box Filter Output")]
    public static void Validate()
    {
        const int allowedItemId = 5;
        const int rejectedItemId = 6;
        List<ulong> exclusiveFilter = new List<ulong>
        {
            1UL << allowedItemId
        };

        Require(
            MapObject.IsItemAllowedByFilterMask(allowedItemId, true, exclusiveFilter),
            "박스 필터가 선택된 아이템을 거부합니다.");
        Require(
            !MapObject.IsItemAllowedByFilterMask(rejectedItemId, true, exclusiveFilter),
            "박스 필터가 선택되지 않은 고정 출력 아이템을 허용합니다.");
        Require(
            MapObject.IsItemAllowedByFilterMask(rejectedItemId, false, exclusiveFilter),
            "초기화되지 않은 박스 필터가 아이템을 제한합니다.");
        Require(
            MapObject.IsItemAllowedByFilterMask(70, true, exclusiveFilter),
            "기존 저장 필터가 이후 추가된 아이템 ID를 차단합니다.");

        Debug.Log("Box filter output validation passed");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new System.InvalidOperationException(message);
        }
    }
}
