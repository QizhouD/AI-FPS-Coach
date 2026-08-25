"""Verify the installed torch build can actually run kernels on this GPU.

`torch.cuda.is_available()` alone is not enough: a wheel built for an older
CUDA can import fine, report a device, and then fail at launch with "no kernel
image is available" on newer architectures such as Blackwell (sm_120).
"""

from __future__ import annotations

import sys


def main() -> int:
    try:
        import torch
    except ImportError as exc:
        print(f"FAIL import torch: {exc}")
        return 1

    print(f"torch {torch.__version__}")
    print(f"built_for_cuda {torch.version.cuda}")

    if not torch.cuda.is_available():
        print("FAIL torch.cuda.is_available() is False")
        return 1

    name = torch.cuda.get_device_name(0)
    major, minor = torch.cuda.get_device_capability(0)
    print(f"device {name}")
    print(f"capability sm_{major}{minor}")

    try:
        supported = torch.cuda.get_arch_list()
    except Exception:
        supported = []
    if supported:
        print(f"arch_list {','.join(supported)}")
        if f"sm_{major}{minor}" not in supported:
            print(
                f"WARN sm_{major}{minor} is not in this wheel's arch list; "
                "it may fall back to JIT or fail outright"
            )

    try:
        a = torch.randn(256, 256, device="cuda")
        result = float((a @ a).sum().item())
        torch.cuda.synchronize()
    except Exception as exc:
        print(f"FAIL gpu matmul: {exc}")
        print("Install a torch build that targets this GPU, e.g. -CudaTag cu128 for Blackwell.")
        return 1

    if result != result:  # NaN guard
        print("FAIL gpu matmul returned NaN")
        return 1

    print("OK gpu compute verified")
    return 0


if __name__ == "__main__":
    sys.exit(main())
