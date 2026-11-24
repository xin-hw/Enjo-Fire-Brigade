using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FireRescue
{
    // Controls and presentation choices for the isolated fire review scene.
    [DefaultExecutionOrder(200)]
    public sealed class FirePhysicsReviewControls : MonoBehaviour
    {
        [SerializeField] FireAirflowSolver airflow;
        [SerializeField] LocalPlayerInput input;
        FireWaterArtView art;

        public FluidSource Focus => art ? art.Focus : null;

        public void Initialize(FireAirflowSolver solver, LocalPlayerInput localInput)
        {
            airflow = solver;
            input = localInput;
        }

        void Start()
        {
            var sources = FindObjectsByType<FluidSource>().Where(s => s.gameObject.scene == gameObject.scene).ToArray();
            var fires = FindObjectsByType<OilPanFireView>().Where(v => v.gameObject.scene == gameObject.scene)
                .Select(v => v.transform).Prepend(airflow.transform).ToArray();
            art = Camera.main.gameObject.AddComponent<FireWaterArtView>();
            art.Initialize(input, sources, GetComponent<WaterLab>(), fires);
            art.Apply();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) airflow.SetTestWind(Vector3.left);
            if (Input.GetKeyDown(KeyCode.Alpha2)) airflow.SetTestWind(Vector3.zero);
            if (Input.GetKeyDown(KeyCode.Alpha3)) airflow.SetTestWind(Vector3.right);
            if (Input.GetKeyDown(KeyCode.R))
            {
#if UNITY_EDITOR
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                    gameObject.scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
                SceneManager.LoadScene(gameObject.scene.name);
#endif
            }
        }

    }
}
