"""Read exact run-owned IDs; verify absence without deleting any resource."""
import json
import os
from pathlib import Path
import re
import subprocess

folder = Path(os.environ["FINDER_JOIN_RESOURCES"])
receipts = list(folder.glob("intranet241-*.json"))
assert len(receipts) == 1, "One owned container cohort required"
receipt = json.loads(receipts[0].read_text(encoding="utf-8"))
assert receipt["state"] == "disposed" and receipt["persistent"] is False
leases = [json.loads(path.read_text(encoding="utf-8")) for path in folder.glob("release-*.json")]
assert len(leases) == 43, "14 joined owners + 28 hosts + one container owner required"
assert all(lease["state"] == "completed" and lease["success"] is True
           and lease["actualOperationCompleted"] is True and lease["reportingErrors"] == 0 for lease in leases), "Unresolved/failed owned release"
ids = [receipt["postgresId"], receipt["redisId"]]
assert len(set(ids)) == 2
for container_id in ids:
    assert isinstance(container_id, str) and re.fullmatch(r"[0-9a-f]{64}", container_id)
    result = subprocess.run(["docker", "container", "inspect", container_id],
                            capture_output=True, text=True, timeout=10, check=False)
    assert result.returncode == 1 and any(message in result.stderr.lower()
                                        for message in ("no such container", "no such object")), "Owned container absence unverified"
output = Path(os.environ["FINDER_JOIN_EVIDENCE"])
output.mkdir(parents=True, exist_ok=True)
(output / "verified-resource-release.json").write_text(json.dumps({
    "owner": receipt["owner"], "containerIds": ids, "verifiedAbsent": 2,
    "persistent": False, "destructiveCleanupByVerifier": False,
}, indent=2), encoding="utf-8")
