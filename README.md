# TIERRA EN LLAMAS

## Colombia: Selva, Ciudad y Conflicto

**Plataforma:** Android (Unity 6 + URP)  
**Género:** Third-Person Shooter / Action-Adventure / Narrativa  
**Versión del GDD:** 2.0  

---

## Descripción

**Tierra en Llamas** es un juego de acción-aventura en tercera persona ambientado en el conflicto armado colombiano. El jugador encarna a un joven guerrillero que debe navegar entre facciones en guerra, tomando decisiones morales que afectan el desarrollo de la historia y determinan uno de cinco finales posibles.

El juego busca el máximo realismo en:
- **Gráficos:** Shaders URP personalizados, post-procesado cinematográfico, clima dinámico
- **Mecánicas:** Balística realista, sistema de cobertura, IA con comportamiento táctico
- **Narrativa:** Diálogos ramificados, sistema de karma tricategórico, 5 finales
- **Ambientación:** Fauna colombiana, música contextual, escenarios auténticos

---

## Estructura del Proyecto

```
TierraEnLlamas/
├── Assets/
│   ├── Scripts/
│   │   ├── GameManager.cs              # Controlador principal del juego
│   │   ├── Player/
│   │   │   ├── PlayerController.cs     # Movimiento, estadísticas, daño localizado
│   │   │   ├── ThirdPersonCamera.cs    # Cámara con modos combate/exploración
│   │   │   └── TouchInputManager.cs    # Controles táctiles Android
│   │   ├── Combat/
│   │   │   ├── WeaponSystem.cs         # Armas con balística realista
│   │   │   └── CombatManager.cs        # Gestión de encuentros y oleadas
│   │   ├── AI/
│   │   │   └── EnemyAI.cs             # IA con pathfinding, cobertura, flanqueo
│   │   ├── Narrative/
│   │   │   ├── NarrativeManager.cs     # Diálogos, misiones, flashbacks
│   │   │   └── KarmaSystem.cs         # Karma + Facciones
│   │   ├── World/
│   │   │   └── WeatherSystem.cs       # Clima dinámico + ciclo día/noche
│   │   ├── Vehicles/
│   │   │   └── VehicleSystem.cs       # Jeep, moto, bote
│   │   ├── Audio/
│   │   │   └── AudioManager.cs        # Audio adaptativo + fauna colombiana
│   │   ├── UI/
│   │   │   └── HUDManager.cs          # HUD completo para Android
│   │   ├── PostProcessing/
│   │   │   └── PostProcessManager.cs  # Efectos cinematográficos
│   │   └── SaveSystem/
│   │       └── SaveSystem.cs          # Guardado cifrado + Google Play
│   ├── Shaders/
│   │   ├── SelvaFoliage.shader        # Vegetación con viento y lluvia
│   │   └── RiverWater.shader          # Agua fluvial realista
│   ├── StreamingAssets/
│   │   └── Data/
│   │       ├── Config/
│   │       │   ├── game_config.json   # Configuración general
│   │       │   └── weapons_config.json # Datos de armas
│   │       ├── Missions/
│   │       │   └── chapter1_missions.json
│   │       └── Dialogues/
│   │           └── chapter1_dialogues.json
│   ├── Scenes/
│   ├── Prefabs/
│   ├── Materials/
│   ├── Textures/
│   ├── Audio/
│   ├── Animations/
│   └── Models/
├── Packages/
├── ProjectSettings/
└── README.md
```

---

## Sistemas Principales

### 1. Sistema de Combate
- Balística realista con caída de bala y penetración
- 9 armas con estadísticas únicas (AK-47, Galil, Dragunov, etc.)
- Sistema de cobertura dinámica
- Daño localizado por zona corporal
- Granadas (fragmentación, humo, molotov)

### 2. Inteligencia Artificial
- Máquina de estados: Patrulla → Alerta → Combate → Búsqueda
- Comportamientos tácticos: flanqueo, supresión, retirada
- Uso inteligente de cobertura
- Comunicación entre unidades
- Reacción al clima y visibilidad

### 3. Sistema Narrativo
- Diálogos ramificados con 2-4 opciones
- Decisiones con temporizador (30s máximo)
- Sistema de karma: Honor + Humanidad
- 5 facciones con relaciones dinámicas
- 5 finales según karma y alianzas
- Flashbacks desbloqueables
- Diario automático del jugador

### 4. Mundo Abierto
- 3 escenarios: Selva del Putumayo, Medellín Nocturno, Bogotá Andina
- Clima dinámico (lluvia tropical, niebla, tormentas)
- Ciclo día/noche (10 min reales = 1 día)
- Vehículos: Jeep, moto, bote fluvial
- Fauna y flora colombiana

### 5. Audio
- Música adaptativa (vallenato, cumbia, tensión)
- Fauna: tucanes, guacamayas, monos aulladores
- Efectos ambientales por escenario
- Radio de época con transmisiones

---

## Paleta de Color por Escenario

| Escenario | Colores Principales | Atmósfera |
|-----------|-------------------|-----------|
| Selva del Putumayo | Verdes intensos, marrones | Húmeda, opresiva |
| Medellín Nocturno | Rojos, naranjas, neón | Peligrosa, vibrante |
| Bogotá Andina | Grises, azules fríos | Fría, tensa |
| Combate | Rojos, naranjas | Caótica, intensa |

---

## Facciones

| Facción | Color | Descripción |
|---------|-------|-------------|
| Frente Amazónico | Verde selva | Guerrilla ideológica |
| Fuerza Nacional | Azul militar | Estado y ejército |
| Los Halcones | Gris | Paramilitares |
| Cartel del Norte | Rojo | Narcotráfico |
| Civiles y ONG | Amarillo | Población civil |

---

## Requisitos Técnicos

- **Motor:** Unity 6 (2025.1+)
- **Render Pipeline:** Universal Render Pipeline (URP)
- **Android mínimo:** 8.0 (API 26)
- **RAM mínima:** 4 GB
- **Almacenamiento:** ~2 GB
- **GPU recomendada:** Adreno 618+ / Mali-G76+

---

## Configuración del Proyecto

1. Abrir con Unity 6 (2025.1 o superior)
2. Importar URP desde Package Manager
3. Importar TextMeshPro
4. Configurar Build Settings para Android
5. Configurar Player Settings:
   - Package Name: `com.tierraenllamas.game`
   - Minimum API Level: 26
   - Target API Level: 34
   - Scripting Backend: IL2CPP
   - Target Architectures: ARM64

---

## Licencia

Proyecto confidencial - GDD v2.0  
© 2025 Tierra en Llamas

---

## Créditos

Diseño y desarrollo basado en el Game Design Document "Tierra en Llamas" v2.0
