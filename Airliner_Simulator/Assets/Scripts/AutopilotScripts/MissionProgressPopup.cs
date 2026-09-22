using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class MissionProgressPopup : MonoBehaviour
{
    [Header("References")]
    public MissionInterface missionInterface;
    public CanvasGroup popupGroup;
    public TMP_Text messageText;

    [Header("Display")]
    [Min(0.1f)]
    public float displaySeconds = 3f;

    [Min(0.01f)]
    public float fadeSeconds = 0.5f;

    public Color waypointColor =
        new Color(0.4f, 1f, 0.65f);

    public Color completeColor =
        new Color(1f, 0.85f, 0.25f);

    private struct Notice
    {
        public string Text;
        public Color Color;

        public Notice(string text, Color color)
        {
            Text = text;
            Color = color;
        }
    }

    private readonly Queue<Notice> notices =
        new Queue<Notice>();

    private MissionInterface subscribedInterface;

    private int lastWaypointIndex = -1;
    private bool completionShown;

    private bool showing;
    private float shownAt;

    private void OnEnable()
    {
        ResetDisplay();

        if (missionInterface == null ||
            popupGroup == null ||
            messageText == null)
        {
            Debug.LogError(
                "MissionProgressPopup: Assign Mission Interface, " +
                "Popup Group, and Message Text in the Inspector.",
                this);

            enabled = false;
            return;
        }

        popupGroup.interactable = false;
        popupGroup.blocksRaycasts = false;
        messageText.raycastTarget = false;

        subscribedInterface = missionInterface;
        subscribedInterface.MissionStartProduced += OnMissionStarted;
    }

    private void OnDisable()
    {
        if (subscribedInterface != null)
        {
            subscribedInterface.MissionStartProduced -= OnMissionStarted;
            subscribedInterface = null;
        }

        ResetDisplay();
    }

    private void OnMissionStarted(UnityMissionRequest request)
    {

        ResetDisplay();
    }

    private void ResetDisplay()
    {
        lastWaypointIndex = -1;
        completionShown = false;

        notices.Clear();
        showing = false;
        shownAt = 0f;

        if (popupGroup != null)
            popupGroup.alpha = 0f;

        if (messageText != null)
            messageText.text = "";
    }

    private void LateUpdate()
    {
        if (missionInterface == null ||
            !missionInterface.isActiveAndEnabled ||
            !missionInterface.MissionRequested)
        {
            ResetDisplay();
            return;
        }

        ObserveMissionProgress();
        UpdateDisplay();
    }

    private void ObserveMissionProgress()
    {
        if (completionShown ||
            !missionInterface.HasReceivedCommand)
        {
            return;
        }

        if (!missionInterface.TryGetGuidance(out _, out _))
            return;

        MissionCommand command = missionInterface.LastReceived;

        if (command.data_status != (byte)DataStatusCode.VALID)
            return;

        int waypointCount = missionInterface.Waypoints.Count;
        int currentIndex = command.current_waypoint_index;

        if (waypointCount <= 0 ||
            currentIndex < 0 ||
            currentIndex >= waypointCount)
        {
            return;
        }

        bool navigating =
            command.mission_state == (byte)MissionStateCode.NAVIGATE;

        bool complete =
            command.mission_state ==
            (byte)MissionStateCode.MISSION_COMPLETE;

        if (!navigating && !complete)
            return;

        if (lastWaypointIndex < 0)
            lastWaypointIndex = currentIndex;

        if (currentIndex > lastWaypointIndex)
        {
            for (int index = lastWaypointIndex;
                 index < currentIndex;
                 index++)
            {
                int passedNumber = index + 1;

                notices.Enqueue(new Notice(
                    $"WAYPOINT {passedNumber} / {waypointCount} PASSED",
                    waypointColor));
            }

            lastWaypointIndex = currentIndex;
        }

        if (complete && currentIndex == waypointCount - 1)
        {
            completionShown = true;

            notices.Enqueue(new Notice(
                "MISSION COMPLETE\n" +
                $"ALL {waypointCount} WAYPOINTS PASSED",
                completeColor));
        }
    }

    private void UpdateDisplay()
    {
        if (!showing && notices.Count > 0)
        {
            Notice next = notices.Dequeue();

            messageText.text = next.Text;
            messageText.color = next.Color;

            popupGroup.alpha = 1f;
            shownAt = Time.unscaledTime;
            showing = true;
        }

        if (!showing)
            return;

        float elapsed = Time.unscaledTime - shownAt;
        float hold = Mathf.Max(0.1f, displaySeconds);
        float fade = Mathf.Max(0.01f, fadeSeconds);

        if (elapsed <= hold)
        {
            popupGroup.alpha = 1f;
            return;
        }

        popupGroup.alpha =
            1f - Mathf.Clamp01((elapsed - hold) / fade);

        if (elapsed >= hold + fade)
        {
            popupGroup.alpha = 0f;
            showing = false;
        }
    }
}