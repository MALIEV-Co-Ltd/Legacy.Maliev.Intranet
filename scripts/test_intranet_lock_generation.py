import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import patch
import tempfile

spec = importlib.util.spec_from_file_location("locks", Path(__file__).with_name("intranet-lock-generation.py"))
locks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(locks)


def graph(packages, framework="net10.0"):
    return json.dumps({"version": 1, "dependencies": {framework: packages}}).encode()


def valid(name):
    row = locks.POLICY[name]
    return {"type": "Transitive", "resolved": row["resolved"], "contentHash": row["contentHash"],
            "dependencies": row["dependencies"]}


class LockDeltaTests(unittest.TestCase):
    def test_qualified_version_changes_pass(self):
        old = {"MassTransit.RabbitMQ": {"resolved": "9.2.2"}}
        new = {"MassTransit.RabbitMQ": valid("MassTransit.RabbitMQ")}
        self.assertEqual(1, len(locks.validate_delta(graph(old), graph(new))))

    def test_unrelated_upgrade_rejected(self):
        with self.assertRaisesRegex(ValueError, "Unrelated"):
            locks.validate_delta(graph({"Other": {"resolved": "1"}}), graph({"Other": {"resolved": "2"}}))

    def test_qualified_version_downgrade_rejected(self):
        with self.assertRaisesRegex(ValueError, "qualified"):
            locks.validate_delta(graph({}), graph({"Scalar.AspNetCore": {"resolved": "2.17.10"}}))

    def test_unrelated_new_dependency_rejected(self):
        with self.assertRaisesRegex(ValueError, "Unrelated"):
            locks.validate_delta(graph({}), graph({"Other": {}}))

    def test_dependency_removal_rejected(self):
        with self.assertRaisesRegex(ValueError, "Unrelated"):
            locks.validate_delta(graph({"Scalar.AspNetCore": {"resolved": "2.17.10"}}), graph({}))

    def test_framework_change_rejected(self):
        with self.assertRaisesRegex(ValueError, "Framework"):
            locks.validate_delta(graph({}), graph({}, "net11.0"))

    def test_duplicate_keys_rejected(self):
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            locks.lock(b'{"version":1,"version":1,"dependencies":{"net10.0":{}}}')

    def test_unknown_top_level_rejected(self):
        with self.assertRaisesRegex(ValueError, "schema"):
            locks.lock(b'{"version":1,"dependencies":{"net10.0":{}},"extra":0}')

    def test_formatter_addition_at_exact_version_passes(self):
        self.assertEqual(1, len(locks.validate_delta(graph({}), graph({"Microsoft.AspNet.WebApi.Client": valid("Microsoft.AspNet.WebApi.Client")}))))

    def test_formatter_arbitrary_version_rejected(self):
        with self.assertRaisesRegex(ValueError, "qualified"):
            locks.validate_delta(graph({}), graph({"Microsoft.AspNet.WebApi.Client": {"resolved": "7.0.0"}}))

    def test_unchanged_graph_retained(self):
        data = graph({"Other": {"resolved": "1", "contentHash": "unchanged"}})
        self.assertEqual([], locks.validate_delta(data, data))

    def test_unrelated_project_dependency_edge_rejected(self):
        old = {"legacy.maliev.servicedefaults": {"type": "Project", "dependencies": {"Other": "1"}}}
        new = {"legacy.maliev.servicedefaults": {"type": "Project", "dependencies": {"Other": "2"}}}
        with self.assertRaisesRegex(ValueError, "edge"):
            locks.validate_delta(graph(old), graph(new))

    def test_existing_formatter_transitive_upgrade_rejected(self):
        with self.assertRaisesRegex(ValueError, "qualified"):
            locks.validate_delta(graph({"Newtonsoft.Json": {"resolved": "13.0.1"}}),
                graph({"Newtonsoft.Json": {"resolved": "13.0.4"}}))

    def test_foreign_custody_is_preserved(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "custody"
            path.mkdir()
            (path / "owner.json").write_text('{}')
            with patch.object(locks, "owner", return_value={"run": "current"}):
                with self.assertRaisesRegex(ValueError, "Foreign"):
                    locks.cleanup_owned(path)
            self.assertTrue(path.exists())

    def test_exact_owned_custody_is_removed(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "custody"
            path.mkdir()
            (path / "owner.json").write_text('{"run":"current"}')
            with patch.object(locks, "owner", return_value={"run": "current"}):
                locks.cleanup_owned(path)
            self.assertFalse(path.exists())

    def test_arbitrary_new_formatter_transitives_rejected(self):
        for name in ("Newtonsoft.Json", "Newtonsoft.Json.Bson", "System.Memory"):
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "qualified"):
                locks.validate_delta(graph({}), graph({name: {"resolved": "99.0.0", "contentHash": "invented"}}))

    def test_forged_package_hash_rejected(self):
        row = valid("Newtonsoft.Json")
        row["contentHash"] = "invented"
        with self.assertRaisesRegex(ValueError, "hash"):
            locks.validate_delta(graph({}), graph({"Newtonsoft.Json": row}))

    def test_forged_package_edges_rejected(self):
        row = valid("Newtonsoft.Json.Bson")
        row["dependencies"] = {"Newtonsoft.Json": "99.0.0"}
        with self.assertRaisesRegex(ValueError, "edges"):
            locks.validate_delta(graph({}), graph({"Newtonsoft.Json.Bson": row}))

    def test_exact_formatter_closure_passes(self):
        names = ("Newtonsoft.Json", "Newtonsoft.Json.Bson", "System.Memory", "System.Threading.Tasks.Extensions")
        self.assertEqual(4, len(locks.validate_delta(graph({}), graph({name: valid(name) for name in names}))))

    def test_wrong_run_attempt_and_source_owners_rejected(self):
        expected = {"GITHUB_RUN_ID": "1", "GITHUB_RUN_ATTEMPT": "2", "EXPECTED_SOURCE_SHA": "a" * 40}
        for key in expected:
            with self.subTest(key=key), tempfile.TemporaryDirectory() as temp:
                path = Path(temp)
                wrong = dict(expected)
                wrong[key] = "wrong"
                (path / "owner.json").write_text(json.dumps(wrong))
                with patch.object(locks, "owner", return_value=expected):
                    with self.assertRaisesRegex(ValueError, "Foreign"):
                        locks.check_owner(path)

    def test_unknown_package_schema_rejected(self):
        row = valid("Newtonsoft.Json")
        row["extra"] = True
        with self.assertRaisesRegex(ValueError, "schema"):
            locks.validate_delta(graph({}), graph({"Newtonsoft.Json": row}))

    def test_retain_rejects_foreign_custody_before_copy(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)
            (path / "owner.json").write_text('{}')
            with patch.object(locks, "identity", return_value="a" * 40), patch.object(locks, "owner", return_value={"run": "current"}):
                with self.assertRaisesRegex(ValueError, "Foreign"):
                    locks.retain(path, path, path / "artifact")
            self.assertFalse((path / "artifact").exists())

    def test_verified_rejects_wrong_source_and_dependency_receipts(self):
        for field in ("source", "defaults", "contracts"):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as temp:
                path = Path(temp)
                receipt = {"source": "a" * 40, "defaults": locks.DEFAULTS, "contracts": locks.CONTRACTS}
                receipt[field] = "b" * 40
                (path / "receipt.json").write_text(json.dumps(receipt))
                with patch.object(locks, "identity", return_value="a" * 40), patch.object(locks, "check_owner"):
                    with self.assertRaisesRegex(ValueError, "identity"):
                        locks.verified(path, path, path)
                self.assertEqual(receipt, json.loads((path / "receipt.json").read_text()))

    def test_project_row_cannot_become_forged_package(self):
        old = {"legacy.maliev.servicedefaults": {"type": "Project", "dependencies": {"MassTransit": "9.2.2"}}}
        new = {"legacy.maliev.servicedefaults": {"type": "Direct", "resolved": "99.0.0", "contentHash": "invented", "dependencies": {"MassTransit": "9.2.3"}}}
        with self.assertRaisesRegex(ValueError, "Project schema"):
            locks.validate_delta(graph(old), graph(new))

    def test_project_self_edge_rejected(self):
        row = json.loads(json.dumps(locks.PROJECT_POLICY))
        row["dependencies"]["legacy.maliev.servicedefaults"] = "99.0.0"
        with self.assertRaisesRegex(ValueError, "edge closure"):
            locks.validate_delta(graph({}), graph({"legacy.maliev.servicedefaults": row}))

    def test_actual_qualified_project_closure_passes(self):
        self.assertEqual(1, len(locks.validate_delta(graph({}), graph({"legacy.maliev.servicedefaults": locks.PROJECT_POLICY}))))

    def test_project_unknown_schema_field_rejected(self):
        row = json.loads(json.dumps(locks.PROJECT_POLICY))
        row["contentHash"] = "invented"
        with self.assertRaisesRegex(ValueError, "Project schema"):
            locks.validate_delta(graph({}), graph({"legacy.maliev.servicedefaults": row}))


if __name__ == "__main__":
    unittest.main()
