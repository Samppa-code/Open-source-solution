import numpy as np
import matplotlib.pyplot as plt
import pyxdf


def extract_marker(row):
    if isinstance(row, (list, tuple, np.ndarray)) and len(row) > 0:
        return row[0]
    return row


def main():
    data, _ = pyxdf.load_xdf("data.xdf") # Change path as needed

    # Change stream indices as needed
    gsr_stream = data[2]
    marker_stream = data[1]
    ecg_stream = data[0]  

    # GSR data
    gsr_ts = np.asarray(gsr_stream["time_stamps"], dtype=float)
    gsr_vals = np.asarray(gsr_stream["time_series"], dtype=float)
    if gsr_vals.ndim > 1:
        gsr_vals = gsr_vals[:, 0]

    # ECG data
    ecg_ts = np.asarray(ecg_stream["time_stamps"], dtype=float)
    ecg_vals = np.asarray(ecg_stream["time_series"], dtype=float)
    if ecg_vals.ndim > 1:
        ecg_vals = ecg_vals[:, 0]

    # Marker data
    marker_ts = np.asarray(marker_stream["time_stamps"], dtype=float)
    marker_rows = np.asarray(marker_stream["time_series"], dtype=object)
    raw_markers = [extract_marker(r) for r in marker_rows]

    # Convert markers to numeric when possible
    converted_markers = []
    for m in raw_markers:
        try:
            converted_markers.append(float(m))
        except Exception:
            converted_markers.append(m)

    # Keep only nonzero markers (treat numeric 0 or string "0" as zero)
    keep_flags = []
    for m in converted_markers:
        if isinstance(m, (int, float)) and m == 0:
            keep_flags.append(False)
        elif isinstance(m, str) and m.strip() == "0":
            keep_flags.append(False)
        else:
            keep_flags.append(True)

    # Ensure lengths match before masking
    n = min(len(marker_ts), len(converted_markers))
    marker_ts = marker_ts[:n]
    converted_markers = converted_markers[:n]
    keep_flags = keep_flags[:n]

    filtered_times = marker_ts[np.array(keep_flags)]
    filtered_markers = [m for m, k in zip(converted_markers, keep_flags) if k]

    # Remove consecutive duplicate markers (keep first of a run)
    def map_marker_value(m):
        try:
            fv = float(m)
            if fv == 10.0:
                return "Experiment start"
            if fv == 20.0:
                return "Experiment end"
        except Exception:
            pass
        return m

    markers = []
    marker_ts = []
    last = None
    for t, m in zip(filtered_times, filtered_markers):
        mapped = map_marker_value(m)
        if mapped != last:
            marker_ts.append(t)
            markers.append(mapped)
            last = mapped

    marker_ts = np.asarray(marker_ts, dtype=float)

    # Align to earliest timestamp
    t0_candidates = []
    if gsr_ts.size:
        t0_candidates.append(gsr_ts[0])
    if ecg_ts.size:
        t0_candidates.append(ecg_ts[0])
    if marker_ts.size:
        t0_candidates.append(marker_ts[0])
    t0 = min(t0_candidates) if t0_candidates else 0.0

    gsr_t_rel = gsr_ts - t0 if gsr_ts.size else np.array([])
    ecg_t_rel = ecg_ts - t0 if ecg_ts.size else np.array([])
    marker_t_rel = marker_ts - t0 if marker_ts.size else np.array([])

    print(f"GSR samples: {gsr_vals.size}, duration: {gsr_t_rel[-1]-gsr_t_rel[0]:.2f}s" if gsr_t_rel.size else "No GSR data")
    print(f"ECG samples: {ecg_vals.size}, duration: {ecg_t_rel[-1]-ecg_t_rel[0]:.2f}s" if ecg_t_rel.size else "No ECG data")
    

    unique_markers = list(dict.fromkeys(markers))
    y_positions = {m: i for i, m in enumerate(unique_markers)}
    marker_y = [y_positions[m] for m in markers]

    # Create subplots: one for GSR + markers, one for ECG + markers
    fig, (ax1, ax3) = plt.subplots(2, 1, figsize=(14, 10), sharex=True)

    # GSR plot
    if gsr_vals.size:
        ax1.plot(gsr_t_rel, gsr_vals, color="tab:blue", linewidth=0.8, label="GSR (µS)")
        ax1.set_ylabel("GSR (µS)", color="tab:blue")
        ax1.tick_params(axis="y", labelcolor="tab:blue")
        ax1.grid(True, alpha=0.3)

    # Markers on GSR plot
    if marker_ts.size:
        ax2 = ax1.twinx()
        ax2.scatter(marker_t_rel, marker_y, marker="|", s=200, linewidths=2, color="tab:red")
        for t, m in zip(marker_t_rel, markers):
            ax2.text(t, y_positions[m] + 0.05, str(m), rotation=45, fontsize=8, ha="left", va="bottom", color="tab:red")
        ax2.set_yticks(list(y_positions.values()))
        ax2.set_yticklabels(list(y_positions.keys()))
        ax2.set_ylabel("Markers", color="tab:red")
        ax2.tick_params(axis="y", labelcolor="tab:red")
        ax2.set_ylim(-0.5, len(unique_markers) - 0.5 if unique_markers else 0.5)

    # ECG plot
    if ecg_vals.size:
        ax3.plot(ecg_t_rel, ecg_vals, color="tab:green", linewidth=0.8, label="ECG")
        ax3.set_ylabel("ECG (µV)", color="tab:green")
        ax3.tick_params(axis="y", labelcolor="tab:green")
        ax3.grid(True, alpha=0.3)

    # Markers on ECG plot
    if marker_ts.size:
        ax4 = ax3.twinx()
        ax4.scatter(marker_t_rel, marker_y, marker="|", s=200, linewidths=2, color="tab:red")
        for t, m in zip(marker_t_rel, markers):
            ax4.text(t, y_positions[m] + 0.05, str(m), rotation=45, fontsize=8, ha="left", va="bottom", color="tab:red")
        ax4.set_yticks(list(y_positions.values()))
        ax4.set_yticklabels(list(y_positions.keys()))
        ax4.set_ylabel("Markers", color="tab:red")
        ax4.tick_params(axis="y", labelcolor="tab:red")
        ax4.set_ylim(-0.5, len(unique_markers) - 0.5 if unique_markers else 0.5)

    plt.xlabel("Time (s, aligned to first sample)")
    plt.suptitle("Combined Plots: GSR & ECG with Markers")
    fig.tight_layout()
    plt.show()


if __name__ == "__main__":
    main()
