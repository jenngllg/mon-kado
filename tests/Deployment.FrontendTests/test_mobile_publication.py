"""Mobile delivery files are explicit public assets, never arbitrary root scripts."""
import unittest
from pathlib import Path
import frontend_contract as contract
import frontend_probe as probe

class MobilePublicationTests(unittest.TestCase):
 def test_exact_mobile_files_are_allowed_but_unreviewed_root_paths_are_refused(self):
  for name in ["push-sw.js","manifest.webmanifest","icons/monkado-192.png","icons/monkado-512.png"]:
   self.assertTrue(contract.allowed_file(name))
  for name in ["evil.js","icons/other.png","icons/../push-sw.js","/push-sw.js","push-sw.js/x","manifest.webmanifest.bak"]:
   self.assertFalse(contract.allowed_file(name))
 def test_mobile_mime_types_and_same_origin_browser_policy_are_explicit(self):
  self.assertEqual({"image/png"},probe.CONTENT_TYPES[".png"])
  self.assertIn("application/manifest+json",probe.CONTENT_TYPES[".webmanifest"])
  policy=(Path(__file__).resolve().parents[2]/"deployments/frontend/frontend.caddy").read_text()
  self.assertIn("manifest-src 'self'",policy)
  self.assertIn("worker-src 'self'",policy)
  self.assertIn("header /manifest.webmanifest >Content-Type application/manifest+json",policy)
