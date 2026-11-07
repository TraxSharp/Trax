Feature: Ordering bread
  A customer fills an order and pays for it. An order with nothing in it is refused.

  Background:
    Given Ada is a customer
    And Ada has an order

  Scenario: An order is paid for and placed
    Given Ada's order has something in it
    When Ada places her order
    Then Ada's order is being paid for
    When the payment goes through
    Then Ada's order is placed
    And Ada's orders show status "Waiting for the oven"

  Scenario: An empty order is refused
    Given Ada's order has nothing in it
    When Ada places her order
    Then Ada is told "Add something first."
    And Ada's order is still a draft

  Scenario: A customer says how many loaves
    When Ada orders "3" loaves for her order
    Then Ada's order is a draft
    And Ada's orders show loaves "3"

  Scenario: A payment for more than a dozen loaves is declined
    When Ada orders "20" loaves for her order
    Then Ada's order is a draft
    When Ada places her order
    Then Ada's order is being paid for
    When the payment is declined
    Then Ada's order was declined
    And Ada's orders show status "Payment declined"

  # The host's payment never fails or is called off by itself: these two run in memory only.
  @in-memory
  Scenario: A payment that cannot be taken leaves the order unpaid
    Given Ada's order has something in it
    When Ada places her order
    Then Ada's order is being paid for
    When the payment cannot be taken
    Then Ada's order is unpaid

  @in-memory
  Scenario: A payment called off leaves the order unpaid
    Given Ada's order has something in it
    When Ada places her order
    Then Ada's order is being paid for
    When the payment is called off
    Then Ada's order is unpaid
    And Ada's orders show status "Payment not taken"
