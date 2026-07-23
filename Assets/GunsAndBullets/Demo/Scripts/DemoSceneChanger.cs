using UnityEngine;
using UnityEngine.SceneManagement;

namespace GNB.Demo
{
    public class DemoSceneChanger : MonoBehaviour
    {
        private float startFixedTimestep = 0.02f;

        private void Awake()
        {
            startFixedTimestep = Time.fixedDeltaTime;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
                LoadScene(1);
            if (Input.GetKeyDown(KeyCode.Alpha2))
                LoadScene(2);
            if (Input.GetKeyDown(KeyCode.Alpha3))
                LoadScene(3);
            if (Input.GetKeyDown(KeyCode.Alpha4))
                LoadScene(4);
            if (Input.GetKeyDown(KeyCode.Alpha5))
                LoadScene(5);
            if (Input.GetKeyDown(KeyCode.Alpha6))
                LoadScene(6);
            if (Input.GetKeyDown(KeyCode.Alpha7))
                LoadScene(7);
            if (Input.GetKeyDown(KeyCode.Alpha8))
                LoadScene(8);
            if (Input.GetKeyDown(KeyCode.Alpha9))
                LoadScene(9);
            if (Input.GetKeyDown(KeyCode.Alpha0))
                LoadScene(10);

            if (Input.GetKeyDown(KeyCode.Escape))
                Application.Quit();
        }

        private async void LoadScene(int index)
        {
            SceneManager.LoadScene(index);
            await Awaitable.NextFrameAsync(destroyCancellationToken);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            Time.fixedDeltaTime = startFixedTimestep;
        }
    }
}
