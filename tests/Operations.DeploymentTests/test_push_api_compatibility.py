"""Only the exact reviewed optional fields and enriched summary may overlap."""
import copy
import hashlib
import json
from pathlib import Path
import unittest
from deploy_runtime import online_compatible, REVIEWED_PUSH_API
from deploy_policy import DeploymentError

class PushAPICompatibilityTests(unittest.TestCase):
 def setUp(self):
  root=Path(__file__).resolve().parents[2]
  document=json.loads((root/"internal/httpapi/specification.json").read_text())
  patch=json.loads((root/"tests/fixtures/push-api-additions.json").read_text())
  self.old,self.new=copy.deepcopy(document),copy.deepcopy(document)
  for section,changes in patch.items():
   a=self.old["paths"] if section=="paths" else self.old["components"]["schemas"]
   z=self.new["paths"] if section=="paths" else self.new["components"]["schemas"]
   for key,change in changes.items():
    if change["before"] is None:a.pop(key,None)
    else:a[key]=change["before"]
    z[key]=change["after"]
  digest=lambda x:hashlib.sha256(json.dumps(x,sort_keys=True,separators=(",", ":")).encode()).hexdigest()
  self.assertEqual(REVIEWED_PUSH_API,(digest(self.old),digest(self.new)))
 def test_exact_additive_contract_is_compatible_and_old_fields_remain(self):
  online_compatible(self.old,self.new)
  for name in ("CreateWishRequest","SaveGroupSuggestionRequest"):
   old=self.old["components"]["schemas"][name];new=self.new["components"]["schemas"][name]
   self.assertEqual(old.get("required"),new.get("required"))
   self.assertTrue(all(new["properties"][key]==value for key,value in old["properties"].items()))
   self.assertNotIn("addedViaExtension",new.get("required",[]))
  summary=self.new["components"]["schemas"]["AdministrativeMemberSummaryResponse"]
  for key,value in self.old["components"]["schemas"]["AdministrativeMemberResponse"]["properties"].items():
   self.assertEqual(value,summary["properties"][key])
 def test_modified_security_missing_operations_and_type_changes_remain_blocked(self):
  for change in ("security","removed","type"):
   candidate=copy.deepcopy(self.new)
   if change=="security":candidate["paths"]["/api/v1/admin/members"]["get"]["security"]=[]
   elif change=="removed":candidate["paths"].pop("/api/v1/admin/members")
   else:candidate["components"]["schemas"]["CreateWishRequest"]["properties"]["name"]={"type":"integer"}
   with self.subTest(change=change),self.assertRaisesRegex(DeploymentError,"ONLINE_API_COMPATIBILITY_REQUIRED"):
    online_compatible(self.old,candidate)
