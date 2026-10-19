CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE "Categories" (
    "Id" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "NormalizedName" character varying(100) NOT NULL,
    CONSTRAINT "PK_Categories" PRIMARY KEY ("Id")
);

CREATE TABLE "Users" (
    "Id" uuid NOT NULL,
    "FullName" character varying(200) NOT NULL,
    "Email" character varying(254) NOT NULL,
    "NormalizedEmail" character varying(254) NOT NULL,
    "PasswordHash" character varying(1000) NOT NULL,
    "ContactPhone" character varying(30) NOT NULL,
    "Role" character varying(20) NOT NULL,
    CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
);

CREATE TABLE "Books" (
    "Id" uuid NOT NULL,
    "OwnerId" uuid NOT NULL,
    "Title" character varying(300) NOT NULL,
    "Author" character varying(200) NOT NULL,
    "Description" character varying(4000) NOT NULL,
    "City" character varying(100) NOT NULL,
    "Status" character varying(20) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Books" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Books_Users_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "BookCategories" (
    "BookId" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    CONSTRAINT "PK_BookCategories" PRIMARY KEY ("BookId", "CategoryId"),
    CONSTRAINT "FK_BookCategories_Books_BookId" FOREIGN KEY ("BookId") REFERENCES "Books" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_BookCategories_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_BookCategories_CategoryId" ON "BookCategories" ("CategoryId");

CREATE INDEX "IX_Books_OwnerId" ON "Books" ("OwnerId");

CREATE UNIQUE INDEX "IX_Categories_NormalizedName" ON "Categories" ("NormalizedName");

CREATE UNIQUE INDEX "IX_Users_NormalizedEmail" ON "Users" ("NormalizedEmail");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261017215014_InitialCreate', '10.0.0');

COMMIT;
