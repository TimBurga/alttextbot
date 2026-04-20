CREATE TABLE "Subscribers" (
    "Did"       varchar(256)             NOT NULL,
    "RKey"      varchar(128)             NOT NULL,
    "Active"    boolean                  NOT NULL DEFAULT true,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Subscribers" PRIMARY KEY ("Did")
);
