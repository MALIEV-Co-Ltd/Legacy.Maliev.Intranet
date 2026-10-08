"""Source-derived POST observation regression; no SDK, Chromium or real request."""
import pathlib
import re
import unittest

ROOT = pathlib.Path(__file__).resolve().parent


class SourceBoundaryTests(unittest.TestCase):
    def test_actual_body_comes_from_owned_clone_before_apply_not_protocol_read(self):
        source = (ROOT / 'SupplierAddressResolvePersistenceTests.cs').read_text()
        self.assertNotIn('await resolved.TextAsync()', source)
        self.assertIn('bodyObserver.ReadAndCloseAsync(timeout.Token)', source)
        self.assertLess(source.index('bodyObserver.InstallAsync'), source.index('var resolved ='))
        self.assertLess(source.index('bodyObserver.ReadAndCloseAsync'), source.index('await apply.ClickAsync()'))
        self.assertIn('JsonSerializer.Serialize(actualResolution), JsonSerializer.Serialize(browserResolution)', source)
        for preserved in ('Assert.Equal(204, updated.Status)', 'await page.ReloadAsync()',
                          'AssertAddress(editedAddress', 'AssertProfile(editedProfile'):
            self.assertIn(preserved, source)

    def test_post_observer_is_exact_bounded_and_preserves_original_promise(self):
        source = (ROOT / 'SupplierAddressBrowserObserver.cs').read_text()
        self.assertIn("method === 'POST'", source)
        self.assertIn('pairs.length === 0', source)
        self.assertIn('const promise = original.apply(this, args)', source)
        self.assertIn('return promise;', source)
        self.assertIn('response.clone()', source)
        self.assertIn('maxBytes = 65536, deadlineMs = 5000', source)
        self.assertIn('JsonDocument.Parse(await closing.WaitAsync(token))', source)
        self.assertNotIn('EvaluateAsync<JsonElement>', source)

    def test_native_controls_are_required_by_workflow_and_retainer(self):
        workflow = (ROOT.parents[1] / '.github/workflows/supplier-address-resolve-persistence.yml').read_text()
        retainer = (ROOT / 'retain.py').read_text()
        self.assertIn('SupplierAddressBrowserObserverControls', workflow)
        self.assertIn('browser_controls_receipts', retainer)
        self.assertIn('browser_observer_release', retainer)
        self.assertIn('address-browser-response-observer', retainer)


if __name__ == '__main__':
    unittest.main()
