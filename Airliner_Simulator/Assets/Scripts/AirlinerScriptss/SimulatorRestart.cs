using UnityEngine;
using UnityEngine.SceneManagement;

public class SimulationRestart : MonoBehaviour
{
    private bool isRestarting;

    public void RestartSimulation()
    {
        if (!Application.isPlaying || isRestarting)
        {
            return;
        }

        Scene currentScene = gameObject.scene;

        if (currentScene.buildIndex < 0)
        {
            Debug.LogError(
                "Restart failed: Add this scene to the build scene list.",
                this
            );
            return;
        }

        isRestarting = true;

        Time.timeScale = 1f;

        try
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                currentScene.buildIndex,
                LoadSceneMode.Single
            );

            if (operation == null)
            {
                isRestarting = false;
                Debug.LogError("Failed to start scene reload.", this);
            }
        }
        catch (System.Exception exception)
        {
            isRestarting = false;
            Debug.LogException(exception, this);
        }
    }
}