using UnityEngine;
using System;

/// <summary>
/// TIERRA EN LLAMAS - VehicleSystem
/// Sistema de vehículos para los tres escenarios:
/// - Jeep: Selva y carreteras (todo terreno)
/// - Moto: Ciudad (ágil, rápida)
/// - Bote: Ríos del Putumayo (navegación fluvial)
/// Incluye física realista, daño al vehículo y persecuciones.
/// </summary>
public class VehicleSystem : MonoBehaviour
{
    [Header("Configuración del Vehículo")]
    public VehicleType Type;
    public VehicleData Data;

    [Header("Estado")]
    public bool IsOccupied { get; private set; }
    public bool IsEngineOn { get; private set; }
    public float CurrentSpeed { get; private set; }
    public float CurrentHealth { get; private set; }
    public float FuelLevel { get; private set; }

    [Header("Física")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform driverSeat;
    [SerializeField] private Transform exitPoint;

    [Header("Ruedas (Jeep/Moto)")]
    [SerializeField] private WheelCollider[] wheelColliders;
    [SerializeField] private Transform[] wheelMeshes;

    [Header("Bote")]
    [SerializeField] private float waterDrag = 2f;
    [SerializeField] private float buoyancy = 10f;
    [SerializeField] private Transform[] buoyancyPoints;

    // Input
    private float throttleInput;
    private float steerInput;
    private float brakeInput;

    // Referencia al jugador
    private PlayerController driver;

    // Eventos
    public event Action OnVehicleEntered;
    public event Action OnVehicleExited;
    public event Action OnVehicleDestroyed;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        CurrentHealth = Data.MaxHealth;
        FuelLevel = Data.MaxFuel;
    }

    private void FixedUpdate()
    {
        if (!IsOccupied || !IsEngineOn) return;

        switch (Type)
        {
            case VehicleType.Jeep:
                UpdateJeepPhysics();
                break;
            case VehicleType.Moto:
                UpdateMotoPhysics();
                break;
            case VehicleType.Bote:
                UpdateBoatPhysics();
                break;
        }

        UpdateFuel();
        UpdateSpeed();
    }

    #region Entrada/Salida

    public void EnterVehicle(PlayerController player)
    {
        if (IsOccupied || CurrentHealth <= 0) return;

        driver = player;
        IsOccupied = true;
        IsEngineOn = true;

        // Desactivar control del jugador
        player.gameObject.SetActive(false);

        // Posicionar en asiento
        // player.transform.SetParent(driverSeat);
        // player.transform.localPosition = Vector3.zero;

        OnVehicleEntered?.Invoke();
        GameManager.Instance.ChangeState(GameState.Playing);

        Debug.Log($"[Vehicle] Jugador entró al {Type}");
    }

    public void ExitVehicle()
    {
        if (!IsOccupied) return;

        IsOccupied = false;
        IsEngineOn = false;

        // Reactivar jugador
        if (driver != null)
        {
            driver.gameObject.SetActive(true);
            driver.transform.position = exitPoint != null ? exitPoint.position : transform.position + transform.right * 2f;
            // driver.transform.SetParent(null);
        }

        driver = null;
        OnVehicleExited?.Invoke();

        Debug.Log($"[Vehicle] Jugador salió del {Type}");
    }

    #endregion

    #region Física del Jeep

    private void UpdateJeepPhysics()
    {
        if (wheelColliders == null || wheelColliders.Length == 0) return;

        float motorTorque = throttleInput * Data.MaxMotorTorque;
        float brakeTorque = brakeInput * Data.MaxBrakeTorque;
        float steerAngle = steerInput * Data.MaxSteerAngle;

        // Aplicar a ruedas delanteras (dirección)
        if (wheelColliders.Length >= 2)
        {
            wheelColliders[0].steerAngle = steerAngle;
            wheelColliders[1].steerAngle = steerAngle;
        }

        // Aplicar motor a ruedas traseras (tracción)
        for (int i = 2; i < wheelColliders.Length; i++)
        {
            wheelColliders[i].motorTorque = motorTorque;
            wheelColliders[i].brakeTorque = brakeTorque;
        }

        // Actualizar meshes de ruedas
        for (int i = 0; i < wheelColliders.Length && i < wheelMeshes.Length; i++)
        {
            Vector3 pos;
            Quaternion rot;
            wheelColliders[i].GetWorldPose(out pos, out rot);
            wheelMeshes[i].position = pos;
            wheelMeshes[i].rotation = rot;
        }

        // Efecto de terreno irregular en selva
        if (GameManager.Instance.CurrentScene.Contains("Putumayo"))
        {
            // Vibración por terreno
            rb.AddForce(Vector3.up * Mathf.Sin(Time.time * 10f) * CurrentSpeed * 0.1f);
        }
    }

    #endregion

    #region Física de la Moto

    private void UpdateMotoPhysics()
    {
        float motorForce = throttleInput * Data.MaxMotorTorque;
        float steerForce = steerInput * Data.MaxSteerAngle;

        // Fuerza hacia adelante
        rb.AddForce(transform.forward * motorForce);

        // Dirección
        float turnAmount = steerForce * CurrentSpeed * 0.01f;
        rb.AddTorque(Vector3.up * turnAmount);

        // Inclinación en curvas
        float targetLean = -steerInput * 30f * Mathf.Clamp01(CurrentSpeed / Data.MaxSpeed);
        Vector3 currentEuler = transform.eulerAngles;
        float currentLean = currentEuler.z > 180 ? currentEuler.z - 360 : currentEuler.z;
        float newLean = Mathf.Lerp(currentLean, targetLean, Time.fixedDeltaTime * 5f);
        transform.rotation = Quaternion.Euler(currentEuler.x, currentEuler.y, newLean);

        // Freno
        if (brakeInput > 0)
        {
            rb.velocity *= (1f - brakeInput * Time.fixedDeltaTime * 3f);
        }
    }

    #endregion

    #region Física del Bote

    private void UpdateBoatPhysics()
    {
        // Flotabilidad
        if (buoyancyPoints != null)
        {
            foreach (var point in buoyancyPoints)
            {
                float waterLevel = GetWaterLevel(point.position);
                if (point.position.y < waterLevel)
                {
                    float depth = waterLevel - point.position.y;
                    Vector3 buoyancyForce = Vector3.up * buoyancy * depth;
                    rb.AddForceAtPosition(buoyancyForce, point.position);
                }
            }
        }

        // Propulsión
        float motorForce = throttleInput * Data.MaxMotorTorque;
        rb.AddForce(transform.forward * motorForce);

        // Dirección del timón
        float turnForce = steerInput * Data.MaxSteerAngle * CurrentSpeed * 0.5f;
        rb.AddTorque(Vector3.up * turnForce);

        // Resistencia del agua
        rb.drag = waterDrag;
        rb.angularDrag = waterDrag * 0.5f;

        // Oleaje suave
        float wave = Mathf.Sin(Time.time * 0.5f + transform.position.x * 0.1f) * 0.3f;
        rb.AddForce(Vector3.up * wave, ForceMode.Acceleration);
    }

    private float GetWaterLevel(Vector3 position)
    {
        // En un juego real, esto consultaría el sistema de agua
        return 0f; // Nivel del agua por defecto
    }

    #endregion

    #region Daño y Combustible

    public void TakeDamage(float damage)
    {
        CurrentHealth -= damage;
        CurrentHealth = Mathf.Max(0, CurrentHealth);

        if (CurrentHealth <= 0)
        {
            DestroyVehicle();
        }
        else if (CurrentHealth < Data.MaxHealth * 0.3f)
        {
            // Vehículo dañado - reducir rendimiento
            // Humo, chispas, etc.
        }
    }

    private void UpdateFuel()
    {
        float consumption = Data.FuelConsumption * Mathf.Abs(throttleInput) * Time.fixedDeltaTime;
        FuelLevel -= consumption;
        FuelLevel = Mathf.Max(0, FuelLevel);

        if (FuelLevel <= 0)
        {
            IsEngineOn = false;
        }
    }

    private void UpdateSpeed()
    {
        CurrentSpeed = rb.velocity.magnitude * 3.6f; // m/s a km/h
    }

    private void DestroyVehicle()
    {
        if (IsOccupied)
        {
            ExitVehicle();
            // Daño al jugador por explosión
            PlayerController.Instance?.Health.TakeDamage(30f, BodyPart.Torso);
        }

        OnVehicleDestroyed?.Invoke();
        // Explosión, fuego, etc.
    }

    #endregion

    #region Input

    public void SetInput(float throttle, float steer, float brake)
    {
        throttleInput = Mathf.Clamp(throttle, -1f, 1f);
        steerInput = Mathf.Clamp(steer, -1f, 1f);
        brakeInput = Mathf.Clamp01(brake);
    }

    #endregion
}

// === DATOS DE VEHÍCULOS ===

public enum VehicleType
{
    Jeep,
    Moto,
    Bote
}

[System.Serializable]
public class VehicleData
{
    public string Name;
    public VehicleType Type;
    public float MaxSpeed;          // km/h
    public float MaxMotorTorque;
    public float MaxBrakeTorque;
    public float MaxSteerAngle;
    public float MaxHealth;
    public float MaxFuel;
    public float FuelConsumption;   // Litros por segundo a máxima aceleración
    public float Mass;

    public static VehicleData CreateJeep()
    {
        return new VehicleData
        {
            Name = "Jeep Willys",
            Type = VehicleType.Jeep,
            MaxSpeed = 80f,
            MaxMotorTorque = 400f,
            MaxBrakeTorque = 600f,
            MaxSteerAngle = 35f,
            MaxHealth = 500f,
            MaxFuel = 50f,
            FuelConsumption = 0.02f,
            Mass = 1500f
        };
    }

    public static VehicleData CreateMoto()
    {
        return new VehicleData
        {
            Name = "Honda XL 200",
            Type = VehicleType.Moto,
            MaxSpeed = 120f,
            MaxMotorTorque = 200f,
            MaxBrakeTorque = 300f,
            MaxSteerAngle = 45f,
            MaxHealth = 200f,
            MaxFuel = 15f,
            FuelConsumption = 0.01f,
            Mass = 150f
        };
    }

    public static VehicleData CreateBote()
    {
        return new VehicleData
        {
            Name = "Lancha Fluvial",
            Type = VehicleType.Bote,
            MaxSpeed = 40f,
            MaxMotorTorque = 300f,
            MaxBrakeTorque = 100f,
            MaxSteerAngle = 25f,
            MaxHealth = 300f,
            MaxFuel = 30f,
            FuelConsumption = 0.03f,
            Mass = 800f
        };
    }
}
