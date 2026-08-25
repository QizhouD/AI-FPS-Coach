"""Download the default CSGO player-detection weights into models/."""

from __future__ import annotations

import argparse
from pathlib import Path

REPO_ID = "keremberke/yolov8m-csgo-player-detection"
FILENAME = "best.pt"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, help="Destination .pt path")
    args = parser.parse_args()
    dest = Path(args.output)
    dest.parent.mkdir(parents=True, exist_ok=True)

    from huggingface_hub import hf_hub_download

    downloaded = Path(
        hf_hub_download(
            repo_id=REPO_ID,
            filename=FILENAME,
            local_dir=str(dest.parent),
        )
    )
    if downloaded.resolve() != dest.resolve():
        dest.write_bytes(downloaded.read_bytes())
        if downloaded.name != dest.name and downloaded.parent.resolve() == dest.parent.resolve():
            downloaded.unlink(missing_ok=True)
    print(dest)


if __name__ == "__main__":
    main()
