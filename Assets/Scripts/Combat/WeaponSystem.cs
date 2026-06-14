using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

/// <summary>
/// TIERRA EN LLAMAS - WeaponSystem
/// Sistema de armas realista con balística, retroceso, dispersión,
/// recarga por tipo de arma y sistema de munición.
/// Armas: Pistola, Fusil (AK-47), Escopeta, Granada, Machete
/// </summary>
public class WeaponSystem : MonoBehaviour
{
    public static WeaponSystem Instance { get; private set; }

    [Header("Arma Actual")]
    public WeaponData CurrentWeapon;
    public int CurrentAmmo;
    public int ReserveAmmo;
    public bool IsReloading { get; private set; }
    public bool IsFiring { get; private set; }

    [Header("Inventario de Armas")]
    public List<WeaponData> WeaponInventory = new List<WeaponData>();
    public int CurrentWeaponIndex = 0;
    public int MaxWeapons = 3;
    public int GrenadeCount = 3;

    [Header("Configuración de Disparo")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private Transform muzzleFlashPoint;
    [SerializeField] private LayerMask hitLayers;
    [SerializeField] private float maxRange = 200f;

    [Header("Retroceso")]
    private float currentRecoil = 0f;
    private float recoilRecoverySpeed = 5f;
    private Vector2 recoilPattern;

    [Header("Efectos")]
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField] private GameObject bulletImpactPrefab;
    [SerializeField] private GameObject bloodImpactPrefab;
    [SerializeField] private TrailRenderer bulletTrailPrefab;

    // Timing
    private float lastFireTime;
    private float fireInterval;
    private Coroutine reloadCoroutine;

    // Eventos
    public event Action<int, int> OnAmmoChanged; // current, reserve
    public event Action<WeaponData> OnWeaponChanged;
    public event Action OnReloadStart;
    public event Action OnReloadEnd;
    public event Action OnFire;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // Inicializar con pistola por defecto
        if (WeaponInventory.Count == 0)
        {
            WeaponInventory.Add(WeaponDatabase.GetWeapon(WeaponType.Pistola));
            WeaponInventory.Add(WeaponDatabase.GetWeapon(WeaponType.FusilAK47));
        }
        EquipWeapon(0);
    }

    private void Update()
    {
        // Recuperar retroceso gradualmente
        if (currentRecoil > 0)
        {
            currentRecoil = Mathf.Lerp(currentRecoil, 0, recoilRecoverySpeed * Time.deltaTime);
        }

        // Disparo automático
        if (IsFiring && CurrentWeapon != null && CurrentWeapon.IsAutomatic)
        {
            TryFire();
        }
    }

    #region Disparo

    public void StartFiring()
    {
        IsFiring = true;
        if (CurrentWeapon != null && !CurrentWeapon.IsAutomatic)
        {
            TryFire();
        }
    }

    public void StopFiring()
    {
        IsFiring = false;
    }

    private void TryFire()
    {
        if (IsReloading) return;
        if (CurrentAmmo <= 0)
        {
            // Click vacío - recargar automáticamente
            Reload();
            return;
        }

        float timeSinceLastFire = Time.time - lastFireTime;
        if (timeSinceLastFire < fireInterval) return;

        Fire();
    }

    private void Fire()
    {
        lastFireTime = Time.time;
        CurrentAmmo--;

        // Calcular dispersión basada en retroceso y estado del jugador
        Vector2 spread = CalculateSpread();

        // Raycast con balística
        Vector3 fireDirection = GetFireDirection(spread);

        if (CurrentWeapon.Type == WeaponType.Escopeta)
        {
            // Escopeta dispara múltiples perdigones
            for (int i = 0; i < CurrentWeapon.PelletsPerShot; i++)
            {
                Vector2 pelletSpread = spread + Random.insideUnitCircle * CurrentWeapon.SpreadAngle;
                Vector3 pelletDir = GetFireDirection(pelletSpread);
                ProcessRaycast(pelletDir, CurrentWeapon.DamagePerPellet);
            }
        }
        else
        {
            ProcessRaycast(fireDirection, CurrentWeapon.Damage);
        }

        // Aplicar retroceso
        ApplyRecoil();

        // Efectos visuales
        PlayMuzzleFlash();
        PlayFireSound();

        // Generar ruido para IA
        GenerateFireNoise();

        // Vibración háptica
        if (GameManager.Instance.Settings.Vibration)
        {
            Handheld.Vibrate();
        }

        // Sacudir cámara
        ThirdPersonCamera.Instance?.Shake(CurrentWeapon.CameraShake, 0.1f);

        OnFire?.Invoke();
        OnAmmoChanged?.Invoke(CurrentAmmo, ReserveAmmo);
    }

    private void ProcessRaycast(Vector3 direction, float damage)
    {
        RaycastHit hit;
        if (Physics.Raycast(firePoint.position, direction, out hit, maxRange, hitLayers))
        {
            // Determinar qué golpeamos
            IDamageable damageable = hit.collider.GetComponent<IDamageable>();

            if (damageable != null)
            {
                // Determinar parte del cuerpo
                BodyPart hitPart = DetermineBodyPart(hit.collider);

                // Calcular daño con caída por distancia
                float distance = hit.distance;
                float distanceModifier = CurrentWeapon.DamageFalloff.Evaluate(distance / maxRange);
                float finalDamage = damage * distanceModifier;

                // Penetración de armadura
                float armorPen = CurrentWeapon.ArmorPenetration;

                damageable.TakeDamage(finalDamage, DamageType.Bullet, hitPart);

                // Efecto de sangre
                SpawnImpact(bloodImpactPrefab, hit.point, hit.normal);
            }
            else
            {
                // Impacto en superficie
                SpawnImpact(bulletImpactPrefab, hit.point, hit.normal);

                // Destrucción de entorno
                IDestructible destructible = hit.collider.GetComponent<IDestructible>();
                if (destructible != null)
                {
                    destructible.TakeDamage(damage);
                }
            }

            // Trail de bala
            SpawnBulletTrail(firePoint.position, hit.point);
        }
        else
        {
            // Trail al vacío
            SpawnBulletTrail(firePoint.position, firePoint.position + direction * maxRange);
        }
    }

    private Vector3 GetFireDirection(Vector2 spread)
    {
        Vector3 baseDirection = firePoint.forward;

        // Aplicar spread
        baseDirection += firePoint.right * spread.x;
        baseDirection += firePoint.up * spread.y;

        return baseDirection.normalized;
    }

    private Vector2 CalculateSpread()
    {
        float baseSpread = CurrentWeapon.BaseSpread;

        // Modificadores
        PlayerController player = PlayerController.Instance;
        if (player != null)
        {
            if (player.IsAiming) baseSpread *= 0.3f;        // Apuntar reduce spread
            if (player.IsCrouching) baseSpread *= 0.7f;     // Agachado reduce spread
            if (player.IsSprinting) baseSpread *= 2.5f;     // Sprint aumenta spread
            if (player.IsInCover) baseSpread *= 0.5f;       // Cobertura reduce spread

            // Heridas en brazos afectan precisión
            baseSpread /= player.Health.GetAccuracyModifier();
        }

        // Retroceso acumulado
        baseSpread += currentRecoil * 0.5f;

        return Random.insideUnitCircle * baseSpread;
    }

    private void ApplyRecoil()
    {
        currentRecoil += CurrentWeapon.RecoilForce;
        currentRecoil = Mathf.Min(currentRecoil, CurrentWeapon.MaxRecoil);

        // Patrón de retroceso (sube y se desvía)
        recoilPattern.y += CurrentWeapon.RecoilForce * 0.8f;
        recoilPattern.x += Random.Range(-1f, 1f) * CurrentWeapon.RecoilForce * 0.3f;

        // Aplicar al input de cámara
        ThirdPersonCamera.Instance?.SetLookInput(-recoilPattern * 0.1f);
    }

    #endregion

    #region Recarga

    public void Reload()
    {
        if (IsReloading) return;
        if (CurrentAmmo >= CurrentWeapon.MagazineSize) return;
        if (ReserveAmmo <= 0) return;

        reloadCoroutine = StartCoroutine(ReloadCoroutine());
    }

    private IEnumerator ReloadCoroutine()
    {
        IsReloading = true;
        OnReloadStart?.Invoke();

        // Animación y sonido de recarga
        // animator.SetTrigger("Reload");
        PlayReloadSound();

        yield return new WaitForSeconds(CurrentWeapon.ReloadTime);

        // Calcular munición
        int ammoNeeded = CurrentWeapon.MagazineSize - CurrentAmmo;
        int ammoToLoad = Mathf.Min(ammoNeeded, ReserveAmmo);

        CurrentAmmo += ammoToLoad;
        ReserveAmmo -= ammoToLoad;

        IsReloading = false;
        OnReloadEnd?.Invoke();
        OnAmmoChanged?.Invoke(CurrentAmmo, ReserveAmmo);
    }

    public void CancelReload()
    {
        if (reloadCoroutine != null)
        {
            StopCoroutine(reloadCoroutine);
            IsReloading = false;
        }
    }

    #endregion

    #region Cambio de Arma

    public void EquipWeapon(int index)
    {
        if (index < 0 || index >= WeaponInventory.Count) return;

        CurrentWeaponIndex = index;
        CurrentWeapon = WeaponInventory[index];
        CurrentAmmo = CurrentWeapon.MagazineSize;
        ReserveAmmo = CurrentWeapon.MaxReserveAmmo;
        fireInterval = 1f / CurrentWeapon.FireRate;

        OnWeaponChanged?.Invoke(CurrentWeapon);
        OnAmmoChanged?.Invoke(CurrentAmmo, ReserveAmmo);
    }

    public void SwitchWeapon()
    {
        if (IsReloading) CancelReload();

        CurrentWeaponIndex = (CurrentWeaponIndex + 1) % WeaponInventory.Count;
        EquipWeapon(CurrentWeaponIndex);
    }

    public void PickupWeapon(WeaponData weapon)
    {
        if (WeaponInventory.Count >= MaxWeapons)
        {
            // Reemplazar arma actual
            WeaponInventory[CurrentWeaponIndex] = weapon;
        }
        else
        {
            WeaponInventory.Add(weapon);
        }
        EquipWeapon(WeaponInventory.Count - 1);
    }

    #endregion

    #region Granada

    public void ThrowGrenade()
    {
        if (GrenadeCount <= 0) return;

        GrenadeCount--;
        // Instanciar granada
        // GameObject grenade = Instantiate(grenadePrefab, firePoint.position, firePoint.rotation);
        // grenade.GetComponent<Rigidbody>().AddForce(firePoint.forward * grenadeThrowForce, ForceMode.Impulse);

        Debug.Log("[WeaponSystem] Granada lanzada. Restantes: " + GrenadeCount);
    }

    #endregion

    #region Melee (Machete)

    public void MeleeAttack()
    {
        // Ataque cuerpo a cuerpo con machete
        float meleeRange = 2.5f;
        float meleeDamage = 80f;
        float meleeAngle = 90f;

        Collider[] hits = Physics.OverlapSphere(transform.position, meleeRange,
            hitLayers);

        foreach (var hit in hits)
        {
            Vector3 dirToTarget = (hit.transform.position - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, dirToTarget);

            if (angle < meleeAngle / 2f)
            {
                IDamageable damageable = hit.GetComponent<IDamageable>();
                if (damageable != null)
                {
                    damageable.TakeDamage(meleeDamage, DamageType.Melee, BodyPart.Torso);
                }
            }
        }

        // Generar ruido moderado
        PlayerController.Instance?.GenerateNoise(0.4f);
    }

    #endregion

    #region Efectos

    private void PlayMuzzleFlash()
    {
        if (muzzleFlash != null)
            muzzleFlash.Play();
    }

    private void PlayFireSound()
    {
        // AudioManager.Instance.PlaySFX(CurrentWeapon.FireSound, transform.position);
    }

    private void PlayReloadSound()
    {
        // AudioManager.Instance.PlaySFX(CurrentWeapon.ReloadSound, transform.position);
    }

    private void SpawnImpact(GameObject prefab, Vector3 position, Vector3 normal)
    {
        if (prefab == null) return;
        GameObject impact = Instantiate(prefab, position, Quaternion.LookRotation(normal));
        Destroy(impact, 3f);
    }

    private void SpawnBulletTrail(Vector3 start, Vector3 end)
    {
        if (bulletTrailPrefab == null) return;
        TrailRenderer trail = Instantiate(bulletTrailPrefab, start, Quaternion.identity);
        StartCoroutine(AnimateTrail(trail, start, end));
    }

    private IEnumerator AnimateTrail(TrailRenderer trail, Vector3 start, Vector3 end)
    {
        float time = 0;
        float duration = 0.05f;

        while (time < duration)
        {
            trail.transform.position = Vector3.Lerp(start, end, time / duration);
            time += Time.deltaTime;
            yield return null;
        }

        trail.transform.position = end;
        Destroy(trail.gameObject, trail.time);
    }

    private void GenerateFireNoise()
    {
        float noiseRadius = CurrentWeapon.NoiseRadius;
        // Alertar enemigos en radio
        Collider[] nearbyEnemies = Physics.OverlapSphere(transform.position, noiseRadius,
            LayerMask.GetMask("Enemy"));

        foreach (var enemy in nearbyEnemies)
        {
            EnemyAI ai = enemy.GetComponent<EnemyAI>();
            if (ai != null)
            {
                ai.AlertToPosition(transform.position);
            }
        }
    }

    #endregion

    #region Utilidades

    private BodyPart DetermineBodyPart(Collider collider)
    {
        // Basado en el nombre del collider o su posición relativa
        string name = collider.name.ToLower();
        if (name.Contains("head")) return BodyPart.Head;
        if (name.Contains("torso") || name.Contains("chest")) return BodyPart.Torso;
        if (name.Contains("arm_l") || name.Contains("leftarm")) return BodyPart.LeftArm;
        if (name.Contains("arm_r") || name.Contains("rightarm")) return BodyPart.RightArm;
        if (name.Contains("leg_l") || name.Contains("leftleg")) return BodyPart.LeftLeg;
        if (name.Contains("leg_r") || name.Contains("rightleg")) return BodyPart.RightLeg;

        return BodyPart.Torso; // Default
    }

    #endregion
}

// === INTERFACES ===

public interface IDamageable
{
    void TakeDamage(float damage, DamageType type, BodyPart part);
    bool IsAlive { get; }
}

public interface IDestructible
{
    void TakeDamage(float damage);
    bool IsDestroyed { get; }
}

public enum DamageType
{
    Bullet,
    Melee,
    Explosion,
    Fire,
    Stealth,
    Environmental
}

// === DATOS DE ARMAS ===

public enum WeaponType
{
    Pistola,
    FusilAK47,
    Escopeta,
    Machete,
    Granada
}

[System.Serializable]
public class WeaponData
{
    public string Name;
    public WeaponType Type;
    public float Damage;
    public float FireRate;           // Disparos por segundo
    public int MagazineSize;
    public int MaxReserveAmmo;
    public float ReloadTime;
    public float BaseSpread;
    public float RecoilForce;
    public float MaxRecoil;
    public float ArmorPenetration;   // 0-1
    public float NoiseRadius;        // Radio de alerta en metros
    public float CameraShake;
    public bool IsAutomatic;
    public AnimationCurve DamageFalloff;

    // Escopeta específico
    public int PelletsPerShot;
    public float DamagePerPellet;
    public float SpreadAngle;

    // Audio
    public string FireSound;
    public string ReloadSound;
    public string EmptySound;
}

/// <summary>
/// Base de datos de armas del juego
/// </summary>
public static class WeaponDatabase
{
    public static WeaponData GetWeapon(WeaponType type)
    {
        switch (type)
        {
            case WeaponType.Pistola:
                return new WeaponData
                {
                    Name = "Beretta 92",
                    Type = WeaponType.Pistola,
                    Damage = 25f,
                    FireRate = 4f,
                    MagazineSize = 15,
                    MaxReserveAmmo = 60,
                    ReloadTime = 1.5f,
                    BaseSpread = 0.02f,
                    RecoilForce = 2f,
                    MaxRecoil = 8f,
                    ArmorPenetration = 0.3f,
                    NoiseRadius = 30f,
                    CameraShake = 0.1f,
                    IsAutomatic = false,
                    DamageFalloff = AnimationCurve.Linear(0, 1, 1, 0.5f),
                    FireSound = "pistol_fire",
                    ReloadSound = "pistol_reload"
                };

            case WeaponType.FusilAK47:
                return new WeaponData
                {
                    Name = "AK-47",
                    Type = WeaponType.FusilAK47,
                    Damage = 35f,
                    FireRate = 10f,
                    MagazineSize = 30,
                    MaxReserveAmmo = 120,
                    ReloadTime = 2.5f,
                    BaseSpread = 0.04f,
                    RecoilForce = 4f,
                    MaxRecoil = 15f,
                    ArmorPenetration = 0.6f,
                    NoiseRadius = 60f,
                    CameraShake = 0.2f,
                    IsAutomatic = true,
                    DamageFalloff = AnimationCurve.Linear(0, 1, 1, 0.3f),
                    FireSound = "ak47_fire",
                    ReloadSound = "ak47_reload"
                };

            case WeaponType.Escopeta:
                return new WeaponData
                {
                    Name = "Escopeta Recortada",
                    Type = WeaponType.Escopeta,
                    Damage = 120f,
                    FireRate = 1.2f,
                    MagazineSize = 6,
                    MaxReserveAmmo = 30,
                    ReloadTime = 3.5f,
                    BaseSpread = 0.1f,
                    RecoilForce = 8f,
                    MaxRecoil = 20f,
                    ArmorPenetration = 0.2f,
                    NoiseRadius = 50f,
                    CameraShake = 0.4f,
                    IsAutomatic = false,
                    PelletsPerShot = 8,
                    DamagePerPellet = 15f,
                    SpreadAngle = 0.15f,
                    DamageFalloff = AnimationCurve.EaseInOut(0, 1, 1, 0.1f),
                    FireSound = "shotgun_fire",
                    ReloadSound = "shotgun_reload"
                };

            default:
                return GetWeapon(WeaponType.Pistola);
        }
    }
}
