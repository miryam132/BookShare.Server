START TRANSACTION;
ALTER TABLE "Books" ADD "Version" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

UPDATE "Books" SET "Version" = gen_random_uuid();

CREATE TABLE "Loans" (
    "Id" uuid NOT NULL,
    "BookId" uuid NOT NULL,
    "BorrowerId" uuid NOT NULL,
    "RecordedByUserId" uuid NOT NULL,
    "BorrowedAtUtc" timestamp with time zone NOT NULL,
    "DueAtUtc" timestamp with time zone NOT NULL,
    "ReturnedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_Loans" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Loans_DueAtUtc" CHECK ("DueAtUtc" > "BorrowedAtUtc"),
    CONSTRAINT "CK_Loans_ReturnedAtUtc" CHECK ("ReturnedAtUtc" IS NULL OR "ReturnedAtUtc" >= "BorrowedAtUtc"),
    CONSTRAINT "FK_Loans_Books_BookId" FOREIGN KEY ("BookId") REFERENCES "Books" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Loans_Users_BorrowerId" FOREIGN KEY ("BorrowerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Loans_Users_RecordedByUserId" FOREIGN KEY ("RecordedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_Loans_ActiveBook" ON "Loans" ("BookId") WHERE "ReturnedAtUtc" IS NULL;

CREATE INDEX "IX_Loans_BorrowerId" ON "Loans" ("BorrowerId");

CREATE INDEX "IX_Loans_RecordedByUserId" ON "Loans" ("RecordedByUserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261018170512_AddLoansAndBookVersion', '10.0.0');

COMMIT;
