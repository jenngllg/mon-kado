import base64
import hashlib
import struct
import unittest

from policy import PerformanceError
from seed import dataset, identifier, password_hash, png


class SeedTests(unittest.TestCase):
    def test_password_encoding_matches_identity_v3(self):
        value = base64.b64decode(password_hash("synthetic-password-only", b"0" * 16))
        self.assertEqual(1, value[0])
        self.assertEqual((2, 100000, 16), struct.unpack(">III", value[1:13]))
        self.assertEqual(hashlib.pbkdf2_hmac("sha512", b"synthetic-password-only", b"0" * 16, 100000, 32), value[-32:])
        for password, salt in (("short", b"0"*16), ("synthetic-password-only", b"0")):
            with self.subTest(password=password), self.assertRaises(PerformanceError):
                password_hash(password, salt)

    def test_dataset_is_bounded_and_refuses_existing_or_unmarked_database(self):
        sql, accounts = dataset("mk816-012345abcdef", "synthetic-password-only", b"0" * 16)
        self.assertEqual(100, len(accounts))
        self.assertEqual(500, sql.count("INSERT INTO public.wishlists"))
        self.assertEqual(10000, sql.count("INSERT INTO public.wishes"))
        self.assertIn("current_database() <> 'mk816_012345abcdef'", sql)
        self.assertIn("EXISTS (SELECT FROM public.users)", sql)
        self.assertTrue(sql.endswith("COMMIT;"))
        self.assertNotIn("synthetic-password-only", sql)
        self.assertEqual(identifier(1, 0), accounts[0]["memberId"])
        self.assertEqual(20, len(accounts[-1]["lists"][-1]["wishes"]))

    def test_image_fixtures_have_valid_dimensions_and_bounded_size(self):
        data = png()
        self.assertTrue(data.startswith(b"\x89PNG"))
        self.assertEqual((1600, 1250), struct.unpack(">II", data[16:24]))
        self.assertLess(len(data), 1024 * 1024)
        for width, height in ((0, 10), (8001, 1), (8000, 8000)):
            with self.subTest(width=width), self.assertRaises(PerformanceError):
                png(width, height)
