import numpy as np
import scipy.io.wavfile as wavfile
import scipy.signal as signal
import os
import gc
import time

# ==============================================================================
#   NEURO-EVOLUTION ENGINE V8.0 (THE ELITE PROTOCOL)
#   High-Performance: 192 kHz / 32-Bit Float
#   - NEU: 111 Hz Carrier (Stimmgabel-Phonophorese Simulation)
#   - NEU: Dynamic Surge (Anti-Habituation)
#   - NEU: SMR Flow, Memory Consolidator & Genius Matrix (Anna Wise)
# ==============================================================================

SAMPLE_RATE = 192000
BIT_DEPTH = np.float32

def get_output_path():
    return os.getcwd()

def save_wav(filename, data):
    full_path = os.path.join(get_output_path(), filename)
    print(f"   [WRITE] {filename}...")
    
    # Safety Normalization (Soft, verzerrungsfrei)
    max_val = np.max(np.abs(data))
    if max_val > 0:
        data = data / max_val * 0.95
        
    wavfile.write(full_path, SAMPLE_RATE, data)
    print("   [DONE] Saved.")

def create_t(duration_minutes):
    """Absolute Phase Precision Time Axis"""
    total_samples = int(SAMPLE_RATE * duration_minutes * 60)
    # ZWINGEND float64! Bei 192kHz stößt float32 nach 15 Minuten an sein Mathe-Limit.
    return np.linspace(0, duration_minutes * 60, total_samples, endpoint=False, dtype=np.float64)

def sin_precise(freq_array_or_val, t):
    """64-Bit Precision Oscillator + Ultra Low-RAM Engine."""
    if np.isscalar(freq_array_or_val):
        phase = t * (2.0 * np.pi * freq_array_or_val) 
    else:
        # IN-PLACE CUMSUM: Spart Gigabytes an RAM (löst den MemoryError)!
        phase = freq_array_or_val.astype(np.float64, copy=False)
        np.cumsum(phase, out=phase)
        phase *= (2.0 * np.pi / SAMPLE_RATE)
        
    np.sin(phase, out=phase) # Überschreibt RAM statt neu zu kopieren
    return phase.astype(BIT_DEPTH, copy=False)

def holographic_pan(audio_mono, t, speed_hz=0.1):
    """Holographic Panning logic (3D Raum)."""
    rot_arg = 2 * np.pi * speed_hz * t
    pan_L = ((np.sin(rot_arg) + 1) / 2).astype(BIT_DEPTH)
    pan_R = ((np.cos(rot_arg) + 1) / 2).astype(BIT_DEPTH)
    distance_mod = (np.sin(rot_arg - np.pi/2) * 0.15 + 0.85).astype(BIT_DEPTH)
    L = audio_mono * pan_L * distance_mod
    R = audio_mono * pan_R * distance_mod
    del pan_L, pan_R, rot_arg, distance_mod
    return L, R

def add_pink_noise(length, volume=0.08):
    noise = np.random.randn(length).astype(BIT_DEPTH)
    b, a = [0.04], [1.0, -0.96] 
    pink = signal.lfilter(b, a, noise).astype(BIT_DEPTH)
    pink *= (volume / np.max(np.abs(pink)))
    return pink

# ==============================================================================
# 1. MORGENS: ACTIVATOR V8 (Dynamic Surge)
#    - 111 Hz Carrier + Wellenbewegung (14Hz -> 40Hz -> 21Hz)
# ==============================================================================
def gen_activator_v8():
    print("\n[1/5] Generating: ACTIVATOR V8 (Dynamic Surge)...")
    duration = 30
    t = create_t(duration)
    
    # Wellenförmiger Beta/Gamma-Fluss (Verhindert Habituation)
    # Pendelt alle 10 Minuten zwischen 14 Hz und 40 Hz
    actual_freq = (27.0 - 13.0 * np.cos(2 * np.pi * (1/600.0) * t)).astype(BIT_DEPTH)
    
    carrier_L = 111.0 # Der magische Phonophorese-Carrier
    freq_R_array = carrier_L + actual_freq
    
    L = sin_precise(carrier_L, t) * 0.5
    R = sin_precise(freq_R_array, t) * 0.5
    
    del actual_freq, freq_R_array, t
    return np.vstack((L, R)).T

# ==============================================================================
# 2. TAGSÜBER: SMR FLOW 2.0 (Fokus & Arbeit)
#    - Ersetzt Alpha. Pendelt zwischen 12.5 Hz und 15 Hz.
# ==============================================================================
def gen_smr_flow():
    print("\n[2/5] Generating: SMR FLOW 2.0 (Focus & PC Work)...")
    duration = 30
    t = create_t(duration)
    
    # Sanftes Pendeln im SMR Bereich für stundenlangen Flow
    smr_freq = (13.75 + 1.25 * np.sin(2 * np.pi * (1/300.0) * t)).astype(BIT_DEPTH)
    
    carrier = sin_precise(111.0, t)
    target_wave = sin_precise(smr_freq, t)
    
    # Isochrone Modulation
    modulator = ((target_wave + 1) * 0.5).astype(BIT_DEPTH)
    carrier *= modulator
    del modulator, target_wave, smr_freq
    
    L, R = holographic_pan(carrier, t, speed_hz=0.05)
    del carrier
    
    pink = add_pink_noise(len(t), volume=0.05)
    L += pink
    R += pink
    del pink, t
    return np.vstack((L, R)).T

# ==============================================================================
# 3. NACHMITTAGS: MEMORY CONSOLIDATOR (LTM)
#    - 17.1 Hz (STM) -> 6 Hz (Speicherung) -> 15 Hz (Wake Up)
# ==============================================================================
def gen_memory_consolidator():
    print("\n[3/5] Generating: MEMORY CONSOLIDATOR (IQ & LTM)...")
    duration = 30
    t = create_t(duration)
    
    samples_10m = int(10 * 60 * SAMPLE_RATE)
    
    freqs = np.zeros(len(t), dtype=BIT_DEPTH)
    # Phase 1: 0-10 Min (15 Hz -> 17.1 Hz)
    freqs[:samples_10m] = np.linspace(15.0, 17.1, samples_10m, dtype=BIT_DEPTH)
    # Phase 2: 10-20 Min (Deep Theta 6 Hz für Hippocampus)
    freqs[samples_10m:2*samples_10m] = 6.0
    # Phase 3: 20-30 Min (Wake Up 15 Hz)
    freqs[2*samples_10m:] = 15.0
    
    # Weiche Übergänge
    b, a = signal.butter(2, 0.0001)
    freqs_smooth = signal.filtfilt(b, a, freqs).astype(BIT_DEPTH)
    del freqs
    
    carrier_L = 111.0
    freq_R_array = carrier_L + freqs_smooth
    
    L = sin_precise(carrier_L, t) * 0.6
    R = sin_precise(freq_R_array, t) * 0.6
    
    del freqs_smooth, freq_R_array, t
    return np.vstack((L, R)).T

# ==============================================================================
# 4. SAMSTAG: GENIUS MATRIX V4 (Anna Wise Protocol)
#    - Spielt Delta, Theta, Alpha und Beta GELERNT gleichzeitig.
# ==============================================================================
def gen_genius_matrix():
    print("\n[4/5] Generating: GENIUS MATRIX V4 (Holistic Intelligence)...")
    duration = 30
    t = create_t(duration)
    
    # Die 4 Gehirnwellen des Genies
    delta = sin_precise(0.5, t)
    theta = sin_precise(5.5, t)
    alpha = sin_precise(11.0, t)
    beta = sin_precise(15.0, t)
    
    # Matrix mixen (phasenstabil)
    matrix = (delta * 0.25) + (theta * 0.25) + (alpha * 0.25) + (beta * 0.25)
    del delta, theta, alpha, beta
    
    # Auf 111 Hz Carrier legen
    carrier = sin_precise(111.0, t)
    modulator = ((matrix + 1) * 0.5).astype(BIT_DEPTH)
    carrier *= modulator
    del matrix, modulator
    
    L, R = holographic_pan(carrier, t, speed_hz=0.08)
    del carrier, t
    return np.vstack((L, R)).T

# ==============================================================================
# 5. NACHTS: DEEP SOMATIC SLEEP (Melatonin Edition)
#    - Ramp-Down 8 Hz -> 2.5 Hz (Schumann & Delta)
# ==============================================================================
def gen_melatonin_sleep():
    print("\n[5/5] Generating: DEEP SOMATIC SLEEP (Melatonin Edition)...")
    duration = 30
    t = create_t(duration)
    
    # Sanfter Abstieg von entspanntem Alpha ins tiefe Delta
    freq_beat = np.linspace(8.0, 2.5, len(t), dtype=BIT_DEPTH)
    
    carrier_L = 111.0
    freq_R_array = carrier_L + freq_beat
    
    L = sin_precise(carrier_L, t) * 0.4
    R = sin_precise(freq_R_array, t) * 0.4
    del freq_beat, freq_R_array
    
    # Meeresrauschen hinzufügen
    noise = np.random.randn(len(t)).astype(BIT_DEPTH)
    b, a = [0.02], [1.0, -0.98] 
    pink = signal.lfilter(b, a, noise).astype(BIT_DEPTH)
    pink *= (0.15 / np.max(np.abs(pink))) 
    
    ocean_mod = ((np.sin(2 * np.pi * 0.1 * t) + 1) * 0.5).astype(BIT_DEPTH)
    pink *= ocean_mod
    
    L += pink
    R += pink
    del pink, ocean_mod, noise, t
    return np.vstack((L, R)).T

# ==============================================================================
# MAIN
# ==============================================================================
if __name__ == "__main__":
    t0 = time.time()
    print("=== NEURO-EVOLUTION ENGINE V8.0 (THE ELITE PROTOCOL) ===")
    print(f"Sample Rate: {SAMPLE_RATE} Hz | Precision: 64-Bit Phase | Base: 111 Hz")
    
    try:
        d = gen_activator_v8()
        save_wav("01_Activator_V8_Dynamic_Surge.wav", d)
        del d; gc.collect()
        
        d = gen_smr_flow()
        save_wav("02_SMR_Flow_PC_Work.wav", d)
        del d; gc.collect()
        
        d = gen_memory_consolidator()
        save_wav("03_Memory_Consolidator_LTM.wav", d)
        del d; gc.collect()
        
        d = gen_genius_matrix()
        save_wav("04_Genius_Matrix_V4.wav", d)
        del d; gc.collect()

        d = gen_melatonin_sleep()
        save_wav("05_Deep_Somatic_Sleep_Melatonin.wav", d)
        del d; gc.collect()
        
    except MemoryError:
        print("\n[!!!] CRITICAL MEMORY ERROR [!!!]")
        print("Schließe andere Programme. Die 192kHz/32-Bit Verarbeitung braucht viel RAM.")

    print(f"\n=== FINISHED in {int(time.time()-t0)}s ===")
    input("Drücke Enter zum Beenden...")
