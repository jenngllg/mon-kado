"""Synthetic SQL only; execution is confined by the runner's ownership guard."""
import base64
import hashlib
import struct
import uuid
import zlib

from policy import require, run_id


def identifier(kind, index):
    # Fixed version-7 fixture identifiers, never production identity material.
    return str(uuid.UUID(f"01996000-{kind:04x}-7000-8000-{index:012x}"))


def password_hash(password, salt):
    require(len(salt) == 16 and len(password) >= 20, "INVALID_FIXTURE_CREDENTIAL")
    key = hashlib.pbkdf2_hmac("sha512", password.encode(), salt, 100000, 32)
    return base64.b64encode(b"\x01" + struct.pack(">III", 2, 100000, 16) + salt + key).decode()


def dataset(identifier_run, password, salt):
    run_id(identifier_run)
    hashed = password_hash(password, salt)
    statements = ["BEGIN;", f"DO $$ BEGIN IF current_database() <> '{identifier_run.replace('-', '_')}' OR EXISTS (SELECT FROM public.users) THEN RAISE EXCEPTION 'UNSAFE_DATABASE'; END IF; END $$;"]
    accounts = []
    for member in range(100):
        user = identifier(1, member)
        email = f"member{member}@mk816.invalid"
        statements.append(f"""INSERT INTO public.users
            (id,user_name,normalized_user_name,email,normalized_email,email_confirmed,password_hash,
             security_stamp,concurrency_stamp,phone_number_confirmed,two_factor_enabled,lockout_enabled,
             access_failed_count,display_name,created_at)
            VALUES ('{user}','{email}','{email.upper()}','{email}','{email.upper()}',true,'{hashed}',
                    '{user}','{user}',false,false,true,0,'Synthetic member {member}',now());""")
        lists = []
        for number in range(5):
            list_index = member * 5 + number
            wishlist = identifier(2, list_index)
            statements.append(f"""INSERT INTO public.wishlists
                (id,owner_id,name,normalized_name,occasion,is_suspended,created_at)
                VALUES ('{wishlist}','{user}','List {number}','LIST {number}','Birthday',false,now());""")
            statements.append(f"UPDATE public.wish_position_sequences SET next_position=20 WHERE wishlist_id='{wishlist}';")
            wishes = []
            for position in range(20):
                wish = identifier(3, list_index * 20 + position)
                statements.append(f"""INSERT INTO public.wishes
                    (id,wishlist_id,name,price,quantity,position,created_at)
                    VALUES ('{wish}','{wishlist}','Synthetic gift {position}',25.00,10,{position+1},now());""")
                wishes.append(wish)
            lists.append({"id": wishlist, "wishes": wishes})
        accounts.append({"memberId": user, "email": email, "lists": lists})
    statements.append("COMMIT;")
    return "\n".join(statements), accounts


def png(width=1600, height=1250, compression=6):
    """Produce a deterministic valid RGB fixture with no imaging dependencies."""
    require(1 <= width <= 8000 and 1 <= height <= 8000 and width * height <= 40_000_000, "INVALID_FIXTURE_SIZE")
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    row = b"\x00" + bytes((40, 120, 180)) * width
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(row * height, compression)) + chunk(b"IEND", b""))
