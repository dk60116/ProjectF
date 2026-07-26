using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraZoom : MonoBehaviour
{
    public float zoomSpeed = 5f; // Скорость приближения/отдаления
    public float minZoom = 1f; // Минимальное значение зума
    public float maxZoom = 10f; // Максимальное значение зума

    void Update()
    {
        // Получаем значение колесика мыши
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        // Изменяем размер приближения/отдаления камеры на основе значения колесика мыши
        float newSize = Camera.main.fieldOfView - scroll * zoomSpeed;

        // Ограничиваем размер приближения/отдаления камеры в пределах minZoom и maxZoom
        newSize = Mathf.Clamp(newSize, minZoom, maxZoom);

        // Применяем новый размер приближения/отдаления камеры
        Camera.main.fieldOfView = newSize;
    }
}