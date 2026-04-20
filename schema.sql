CREATE TABLE subscribers (
    did        varchar(256)             NOT NULL,
    r_key      varchar(128)             NOT NULL,
    active     boolean                  NOT NULL DEFAULT true,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_subscribers PRIMARY KEY (did)
);
