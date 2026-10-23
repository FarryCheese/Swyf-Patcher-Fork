# SwyfPatcher — AI & Multiplayer Host Offload Mod for Scam With Your Friends

[English](#english) | [Русский](#русский)

---

<a name="русский"></a>
## 🇷🇺 Русский

Комплексный IL-патчер и мод для игры **Scam With Your Friends** (версия `0.1.83-swyf` и новее), который решает проблемы с ИИ-голосом (Qwen3 TTS), лагами микрофона, нехваткой видеопамяти и позволяет **полностью переносить вычисления нейросети с ПК слабых клиентов на ПК хоста лобби**.

### 🌟 Основные возможности

1. **Host-Side TTS Offload (Мультиплеер без лагов у друзей):**
   - В оригинале игра заставляет каждый клиент (даже со слабыми 4 ГБ VRAM) запускать `QwenTtsWorker.exe` (~2.7 ГБ памяти) для своего стола.
   - С этим патчем Хост перехватывает генерацию всех звонков лобби. ПК друга вообще **не запускает** тяжелые процессы нейросетей, а Хост в реальном времени транслирует готовый голос по сети через Opus-стриминг прямо в телефон клиента.
2. **Предбуферизация предложений (Чёткая речь без заиканий):**
   - На слабых ПК игра начинает воспроизводить речь раньше, чем успевает сгенерировать фразу, из-за чего голос заикается и обрывается.
   - Патч вводит режим ожидания полной генерации фразы — персонаж выдерживает небольшую реалистичную паузу раздумий и выдаёт цельное, плавное предложение.
3. **Гибридный режим (GPU + CPU):**
   - Возможность оставить быструю генерацию голоса (Qwen3) на видеокарте (`Vulkan0`), а распознавание микрофона (Whisper / Moonshine STT) перенести на CPU, разгружая видеопамять.
   - Доступен и режим чистого CPU для карточек с малым объемом VRAM.
4. **Снятие лимитов и защита от сбоев:**
   - Снят порог обязательных 8 ГБ VRAM (`HardwareEligible`).
   - Увеличены таймауты загрузки и генерации до 120–300 секунд (игра больше не отключает русскую озвучку при первом микрофризе).
   - Ёмкость буфера `BlockingCollection` увеличена с 32 до 65536 для устранения дедлоков потоков генерации.
   - Задержка тишины микрофона снижена с 1.25 с до 0.65 с для мгновенного отклика.
5. **Безопасность и идемпотентность:**
   - Перед первым изменением игра делает резервные копии оригинальных библиотек (`.bak`).
   - Любое повторное применение патча всегда читает чистый `.bak`-файл.

---

### 🚀 Быстрый старт (Установка)

1. **Полностью закройте игру.**
2. Скачайте архив со страницы [Releases](../../releases) и распакуйте в любую удобную папку.
3. Запустите файл **`PATCH.bat`**.
4. В открывшемся интерактивном меню настройте нужные параметры (клавишами `1`, `2`, `3`) или просто нажмите **Enter** для применения рекомендуемых настроек:
   - `[1] Вычисления в лобби` : **ВСЕ НА ХОСТА** / Обычные
   - `[2] Время ответа NPC`   : **Дожидаться полной генерации (без заиканий)** / Сразу по ходу генерации
   - `[3] Режим оборудования` : **Обычная (GPU)** / **Гибрид (GPU+CPU)** / **Только CPU**
5. Дождитесь надписи `[OK] Готово!` и запускайте игру.

> **Примечание:** Если игра установлена на нестандартном диске или пути, просто перетащите папку игры мышкой на `PATCH.bat`.

---

### ⚙️ Флаги командной строки (для автоматизации)

Вы можете запускать патчер напрямую через консоль без интерактивного меню:
```cmd
PATCH.bat [путь_к_игре] [опции]
```
**Доступные опции:**
- `--host-tts` / `--vanilla-tts` — перенос вычислений на хоста / стандартный режим
- `--prebuffer` / `--instant-speech` — ждать полной генерации / говорить сразу
- `--gpu` / `--hybrid` / `--cpu` — режим оборудования

---

### 🔄 Как удалить патч (Откат)

- **Через Steam:** ПКМ по игре в Steam → *Свойства* → *Установленные файлы* → *Проверить целостность файлов*.
- **Вручную:** В папке игры `Scam With Your Friends_Data\Managed\` переименуйте `Assembly-CSharp.dll.bak` и `ScriptsAssDef.dll.bak` обратно в `.dll`.

---

<a name="english"></a>
## 🇬🇧 English

Comprehensive IL bytecode patcher and mod for **Scam With Your Friends** (build `0.1.83-swyf` and newer) that addresses AI voice (Qwen3 TTS) stuttering, VRAM limits, microphone latency, and enables **full neural network computation offloading from low-spec clients to the Lobby Host**.

### 🌟 Key Features

1. **Host-Side TTS Offload (Lag-Free Multiplayer for Friends):**
   - Vanilla game forces each client to run local TTS worker processes (~2.7 GB VRAM/RAM) for their desk.
   - This mod intercepts TTS on the Host server, computes audio entirely on the Host PC, and streams real-time Opus audio packets to the clients. Friends with weak PCs never spawn heavy AI worker processes!
2. **Sentence Pre-Buffering (Smooth, Stutter-Free Voice):**
   - Prevents audio dropouts and stuttering by ensuring full sentences are generated before playback begins.
3. **Hybrid Hardware Mode (GPU + CPU):**
   - Keeps fast TTS synthesis on the GPU while offloading Whisper/Moonshine microphone STT to the CPU, freeing up VRAM for game graphics.
   - Also includes a pure CPU mode for systems with extremely low VRAM.
4. **Relaxed Hardware Checks & Extended Timeouts:**
   - Removes strict 8 GB VRAM check.
   - Extends timeouts from 12–20s to 120–300s to prevent AI engine shutdowns during shader compilation or heavy loads.
   - Increases audio frame collection queue capacity from 32 to 65536 to prevent deadlocks.
   - Tightens microphone silence finalization from 1.25s to 0.65s for snappier dialogue response.
5. **Safe & Idempotent:**
   - Automatically creates `.bak` backups of original game DLLs on first run and always patches from pristine backups.

---

### 🚀 Installation

1. **Close the game completely.**
2. Download the release archive from [Releases](../../releases) and extract it anywhere.
3. Run **`PATCH.bat`**.
4. Use the interactive menu to toggle options (`1`, `2`, `3`) or simply press **Enter** to apply recommended defaults:
   - `[1] Multiplayer Compute` : **ALL ON HOST** / Vanilla
   - `[2] NPC Response Timing` : **Full sentence pre-buffer** / Instant streaming
   - `[3] Hardware Processing` : **GPU** / **Hybrid (GPU+CPU)** / **CPU-Only**
5. Wait for `[OK] Done!` and launch the game.

> **Tip:** If your game is in a non-default library, drag and drop the game folder directly onto `PATCH.bat`.

---

### 🔄 Uninstallation

- **Via Steam:** Right-click game in Steam → *Properties* → *Installed Files* → *Verify integrity of game files*.
- **Manually:** In `Scam With Your Friends_Data\Managed\`, restore `Assembly-CSharp.dll.bak` and `ScriptsAssDef.dll.bak`.

---

### 📜 License & Disclaimer
This is an unofficial community modification. Not affiliated with Jatater Worldwide. Use at your own discretion.
Licensed under the [MIT License](LICENSE).
