ALTER TABLE discoveries ADD COLUMN document_id INTEGER REFERENCES documents(id);
CREATE INDEX idx_discoveries_document ON discoveries(document_id);
