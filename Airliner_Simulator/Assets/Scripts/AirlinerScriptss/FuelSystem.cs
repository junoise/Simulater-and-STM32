using UnityEngine;
using TMPro;

public class FuelSystem : MonoBehaviour
{
    [Header("Fuel and Weight Settings (Unit: kg)")]
    public float maxFuelMass = 20000f;       
    public float currentFuelMass;            

    [Header("Fuel Consumption Rate Setting (kg/sec)")]
    public float idleBurnRate = 0.5f;        
    public float maxBurnRate = 10f;          
    public float emptyWeight = 42000f;       

    [Header("Status Flags")]
    public bool isLowFuelWarning = false;
    public bool isEngineStarved = false;

    [Header("UI and Component Connections")]
    public TextMeshProUGUI fuelUIText;
    public Rigidbody aircraftRb;
    public AirlinerPhysics physics;

    void Start()
    {
        currentFuelMass = maxFuelMass;
        if (physics == null)
        {
            physics = GetComponent<AirlinerPhysics>();
        }
    }

    void Update()
    {
        if (currentFuelMass > 0 && !isEngineStarved)
        {
            float currentThrottle = (physics != null) ? physics.throttle : 0f;

            float actualBurnRate = idleBurnRate + (maxBurnRate * currentThrottle);

            currentFuelMass -= actualBurnRate * Time.deltaTime;

            if (currentFuelMass <= 0)
            {
                currentFuelMass = 0;
                isEngineStarved = true;
                Debug.LogWarning("Engine Shutdown: The fuel has been completely depleted!");
            }

            UpdateAircraftMass();
        }

        UpdateUI();
    }

    void UpdateAircraftMass()
    {
        if (aircraftRb != null)
        {
            aircraftRb.mass = emptyWeight + currentFuelMass;
        }
    }

    void UpdateUI()
    {
        if (fuelUIText != null)
        {
            fuelUIText.text = "FUEL: " + Mathf.Round(currentFuelMass).ToString() + " kg";

            if (currentFuelMass <= maxFuelMass * 0.2f)
            {
                isLowFuelWarning = true;
                fuelUIText.color = Color.red;
            }
            else
            {
                isLowFuelWarning = false;       
            }
        }
    }
}