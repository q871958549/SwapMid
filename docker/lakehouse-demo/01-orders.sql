CREATE TABLE IF NOT EXISTS orders (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    order_no VARCHAR(64) NOT NULL,
    customer_name VARCHAR(128) NOT NULL,
    amount DECIMAL(18, 2) NOT NULL,
    status VARCHAR(32) NOT NULL,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

INSERT INTO orders (order_no, customer_name, amount, status)
VALUES
    ('ORD-2026-0001', 'Acme Logistics', 1250.00, 'PAID'),
    ('ORD-2026-0002', 'Northwind Retail', 480.50, 'PENDING'),
    ('ORD-2026-0003', 'Contoso Manufacturing', 3199.90, 'SHIPPED');
